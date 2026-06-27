using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// 外部ネイティブライブラリに依存しない、読み取り専用の最小SQLiteリーダー。
    /// テーブルB-Tree（リーフ/内部ページ）とオーバーフローページのみを扱い、
    /// 必要なテーブルをフルスキャンしてレコードを取り出す。
    /// </summary>
    public sealed class MiniSqlite
    {
        /// <summary>テーブルの1行。列名から値を引ける。</summary>
        public sealed class Row
        {
            public long m_RowId = 0;
            public readonly Dictionary<string, object> Columns = new Dictionary<string, object>();

            public string GetString(string column)
            {
                var found = Columns.TryGetValue(column, out var value);
                if (!found || value == null) {
                    return null;
                }
                return value.ToString();
            }

            public long GetLong(string column, long fallback)
            {
                var found = Columns.TryGetValue(column, out var value);
                if (found && value is long longValue) {
                    return longValue;
                }
                return fallback;
            }
        }

        // ---- SQLiteファイルフォーマットの定数 ----

        // ファイル先頭のDBヘッダのサイズ。ページ1のみこの直後からページヘッダが始まる
        private const int c_DatabaseHeaderSize = 100;
        // ページ種別（ページヘッダの先頭1バイト）。テーブルB-Treeの2種類のみ扱う
        private const byte c_InteriorTablePage = 5;
        private const byte c_LeafTablePage = 13;

        // シリアル型1〜6に対応する整数のバイト数
        private static readonly int[] m_IntegerByteCounts = { 0, 1, 2, 3, 4, 6, 8 };
        // 列定義ではなくテーブル制約の行を見分けるためのキーワード
        private static readonly HashSet<string> m_ConstraintKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "KEY"
        };

        private readonly byte[] r_Bytes;
        private readonly int r_PageSize;
        private readonly int r_UsableSize;
        private readonly Dictionary<string, int> r_RootPages = new Dictionary<string, int>();
        private readonly Dictionary<string, List<string>> r_ColumnNames = new Dictionary<string, List<string>>();

        public MiniSqlite(string path)
        {
            r_Bytes = ReadAllBytesWithRetry(path);
            var magic = Encoding.ASCII.GetString(r_Bytes, 0, 15);
            if (magic != "SQLite format 3") {
                throw new InvalidDataException("SQLiteデータベースではありません: " + path);
            }

            // ページサイズはDBヘッダのオフセット16。値1は65536を意味する（フォーマット仕様）
            var rawPageSize = ReadUInt16BE(16);
            r_PageSize = rawPageSize;
            if (rawPageSize == 1) {
                r_PageSize = 65536;
            }
            // ページ末尾の予約領域（オフセット20）を除いた部分が実際に使える
            var reserved = r_Bytes[20];
            r_UsableSize = r_PageSize - reserved;

            ParseSchema();
        }

        private static byte[] ReadAllBytesWithRetry(string path)
        {
            const int retryCount = 8;
            var retryDelay = TimeSpan.FromSeconds(0.2);

            for (var attempt = 0; attempt < retryCount; attempt += 1) {
                try {
                    return ReadAllBytesAllowingExternalWrites(path);
                }
                catch (FileNotFoundException) {
                    throw;
                }
                catch (DirectoryNotFoundException) {
                    throw;
                }
                catch (IOException) {
                    var hasMoreAttempts = attempt + 1 < retryCount;
                    if (!hasMoreAttempts) {
                        throw;
                    }
                    Thread.Sleep(retryDelay);
                }
            }
            throw new IOException("SQLiteデータベースを読み込めませんでした: " + path);
        }

        private static byte[] ReadAllBytesAllowingExternalWrites(string path)
        {
            // FileAccess.Readのみで開く。FileShare.Writeは既存のBLM/SQLite書き込みハンドルとの共有許可。
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete);
            using var memory = new MemoryStream();
            source.CopyTo(memory);
            return memory.ToArray();
        }

        public bool HasTable(string table)
        {
            return r_RootPages.ContainsKey(table);
        }

        /// <summary>指定テーブルの全行を返す（フルスキャン）。</summary>
        public List<Row> SelectAll(string table)
        {
            var result = new List<Row>();
            if (r_RootPages.TryGetValue(table, out var root)) {
                if (!r_ColumnNames.TryGetValue(table, out var columns)) {
                    columns = new List<string>();
                }
                WalkTablePage(root, columns, result);
            }
            return result;
        }

        // ---- スキーマ（sqlite_master）解析 ----

        private void ParseSchema()
        {
            // sqlite_master のルートはページ1。列順: type,name,tbl_name,rootpage,sql
            var masterColumns = new List<string> { "type", "name", "tbl_name", "rootpage", "sql" };
            var masterRows = new List<Row>();
            WalkTablePage(1, masterColumns, masterRows);

            foreach (var row in masterRows) {
                var isTable = row.GetString("type") == "table";
                var name = row.GetString("name");
                var rootPage = (int)row.GetLong("rootpage", 0);
                if (isTable && !string.IsNullOrEmpty(name) && rootPage > 0) {
                    r_RootPages[name] = rootPage;
                    r_ColumnNames[name] = ParseColumnNames(row.GetString("sql"));
                }
            }
        }

        /// <summary>CREATE TABLE文から列名を順序通りに抽出する。</summary>
        private static List<string> ParseColumnNames(string createSql)
        {
            if (string.IsNullOrEmpty(createSql)) {
                return new List<string>();
            }
            var open = createSql.IndexOf('(');
            var close = createSql.LastIndexOf(')');
            if (open < 0 || close <= open) {
                return new List<string>();
            }

            // 括弧内をカンマで列・制約の定義に分け、列定義の先頭トークン（列名）だけを集める
            var body = createSql.Substring(open + 1, close - open - 1);
            return SplitTopLevel(body)
                .Select(definition => ReadFirstToken(definition.Trim()))
                .Where(token => token.Length > 0 && !m_ConstraintKeywords.Contains(token))
                .ToList();
        }

        // 括弧の深さを考慮してトップレベルのカンマで分割する
        private static List<string> SplitTopLevel(string text)
        {
            var parts = new List<string>();
            var builder = new StringBuilder();
            var depth = 0;
            foreach (var ch in text) {
                if (ch == '(') {
                    depth += 1;
                }
                else if (ch == ')') {
                    depth -= 1;
                }

                if (ch == ',' && depth == 0) {
                    parts.Add(builder.ToString());
                    builder.Length = 0;
                } else {
                    builder.Append(ch);
                }
            }
            parts.Add(builder.ToString());
            return parts;
        }

        // 列定義の先頭トークン（列名）を取り出し、引用記号を除去する
        private static string ReadFirstToken(string definition)
        {
            var quoteChars = "\"`[]'";
            var builder = new StringBuilder();
            foreach (var ch in definition) {
                var isSeparator = char.IsWhiteSpace(ch) || ch == '(';
                if (isSeparator && builder.Length > 0) {
                    break;
                }
                var isQuote = quoteChars.IndexOf(ch) >= 0;
                if (!isSeparator && !isQuote) {
                    builder.Append(ch);
                }
            }
            return builder.ToString();
        }

        // ---- B-Tree走査 ----

        private void WalkTablePage(int pageNumber, List<string> columns, List<Row> output)
        {
            var pageStart = (pageNumber - 1) * r_PageSize;
            // ページ1のみ先頭にDBヘッダが乗るため、その直後がページヘッダになる
            var headerOffset = pageStart;
            if (pageNumber == 1) {
                headerOffset = pageStart + c_DatabaseHeaderSize;
            }

            var pageType = r_Bytes[headerOffset];
            if (pageType == c_InteriorTablePage) {
                WalkInteriorPage(pageStart, headerOffset, columns, output);
            }
            if (pageType == c_LeafTablePage) {
                WalkLeafPage(pageStart, headerOffset, columns, output);
            }
        }

        // 内部ページ: 各セルが指す左子ページと、ヘッダ末尾の右端ページをすべて辿る
        private void WalkInteriorPage(int pageStart, int headerOffset, List<string> columns, List<Row> output)
        {
            var cellCount = ReadUInt16BE(headerOffset + 3);
            // 内部ページのヘッダは12バイト（共通8バイト+右端ページ番号4バイト）。その直後にセル位置の配列が並ぶ
            var cellPointerArray = headerOffset + 12;
            for (var i = 0; i < cellCount; i += 1) {
                var cellOffset = pageStart + ReadUInt16BE(cellPointerArray + i * 2);
                var leftChild = (int)ReadUInt32BE(cellOffset);
                WalkTablePage(leftChild, columns, output);
            }
            var rightMost = (int)ReadUInt32BE(headerOffset + 8);
            WalkTablePage(rightMost, columns, output);
        }

        // リーフページ: 各セルが1行分のレコードを持つ
        private void WalkLeafPage(int pageStart, int headerOffset, List<string> columns, List<Row> output)
        {
            var cellCount = ReadUInt16BE(headerOffset + 3);
            // リーフページのヘッダは8バイト。その直後にセル位置の配列が並ぶ
            var cellPointerArray = headerOffset + 8;
            for (var i = 0; i < cellCount; i += 1) {
                var cellOffset = pageStart + ReadUInt16BE(cellPointerArray + i * 2);
                ReadLeafCell(cellOffset, columns, output);
            }
        }

        // セル構造: ペイロード長(varint) + rowid(varint) + レコード本体
        private void ReadLeafCell(int cellOffset, List<string> columns, List<Row> output)
        {
            var cursor = cellOffset;
            var payloadLength = ReadVarint(ref cursor);
            var rowId = ReadVarint(ref cursor);
            var payload = ReadPayload(cursor, payloadLength);

            var row = new Row {
                m_RowId = rowId
            };
            ParseRecord(payload, columns, row);
            output.Add(row);
        }

        // ローカル領域とオーバーフローページを連結して完全なペイロードを得る
        private byte[] ReadPayload(int localStart, long payloadLength)
        {
            var maxLocal = r_UsableSize - 35;
            if (payloadLength <= maxLocal) {
                var local = new byte[payloadLength];
                Array.Copy(r_Bytes, localStart, local, 0, (int)payloadLength);
                return local;
            }

            // ページ内に置かれる先頭部分のサイズを求める（SQLite仕様の計算式）
            var minLocal = (r_UsableSize - 12) * 32 / 255 - 23;
            var k = minLocal + (payloadLength - minLocal) % (r_UsableSize - 4);
            var localBytes = (long)minLocal;
            if (k <= maxLocal) {
                localBytes = k;
            }

            var payload = new byte[payloadLength];
            Array.Copy(r_Bytes, localStart, payload, 0, (int)localBytes);

            // 残りは「次ページ番号(4バイト)+データ」が連なるオーバーフローページを辿って読む
            var written = localBytes;
            var nextPage = (int)ReadUInt32BE(localStart + (int)localBytes);
            while (nextPage != 0 && written < payloadLength) {
                var pageStart = (nextPage - 1) * r_PageSize;
                var available = r_UsableSize - 4;
                var copyCount = (int)Math.Min(payloadLength - written, available);
                Array.Copy(r_Bytes, pageStart + 4, payload, (int)written, copyCount);
                written += copyCount;
                nextPage = (int)ReadUInt32BE(pageStart);
            }
            return payload;
        }

        // ---- レコード（行）デコード ----

        // レコード構造: ヘッダ長(varint) + 各列のシリアル型(varint)... + 各列の値...
        private static void ParseRecord(byte[] payload, List<string> columns, Row row)
        {
            var cursor = 0;
            var headerEnd = (int)ReadVarint(payload, ref cursor);

            var serialTypes = new List<long>();
            while (cursor < headerEnd) {
                serialTypes.Add(ReadVarint(payload, ref cursor));
            }

            var bodyCursor = headerEnd;
            for (var i = 0; i < serialTypes.Count; i += 1) {
                var value = ReadValue(payload, serialTypes[i], ref bodyCursor);
                var columnName = "col" + i;
                if (i < columns.Count) {
                    columnName = columns[i];
                }
                row.Columns[columnName] = value;
            }
        }

        // シリアル型に従って1列分の値を読み出す
        private static object ReadValue(byte[] payload, long serialType, ref int cursor)
        {
            // 0: NULL
            if (serialType == 0) {
                return null;
            }
            // 1〜6: 符号付き整数（型ごとにバイト数が決まっている）
            if (serialType <= 6) {
                var count = m_IntegerByteCounts[(int)serialType];
                var value = ReadSignedBE(payload, cursor, count);
                cursor += count;
                return value;
            }
            // 7: 64bit浮動小数点数
            if (serialType == 7) {
                var raw = ReadSignedBE(payload, cursor, 8);
                cursor += 8;
                return BitConverter.Int64BitsToDouble(raw);
            }
            // 8と9: 値領域を持たない定数の0と1
            if (serialType == 8) {
                return 0L;
            }
            if (serialType == 9) {
                return 1L;
            }

            // 13以上の奇数: テキスト、12以上の偶数: BLOB。長さはシリアル型の値から求める
            var isText = (serialType & 1) == 1;
            if (isText) {
                var textLength = (int)((serialType - 13) / 2);
                var text = Encoding.UTF8.GetString(payload, cursor, textLength);
                cursor += textLength;
                return text;
            }
            var blobLength = (int)((serialType - 12) / 2);
            var blob = new byte[blobLength];
            Array.Copy(payload, cursor, blob, 0, blobLength);
            cursor += blobLength;
            return blob;
        }

        private static long ReadSignedBE(byte[] data, int offset, int count)
        {
            var value = 0L;
            for (var i = 0; i < count; i += 1) {
                value = (value << 8) | data[offset + i];
            }
            // 最上位ビットが立っていれば符号拡張する（8バイトはシフトで桁あふれ済みのため不要）
            var signBit = (long)1 << (count * 8 - 1);
            var isNegative = (value & signBit) != 0;
            if (isNegative && count < 8) {
                var range = (long)1 << (count * 8);
                return value - range;
            }
            return value;
        }

        // ---- バイト読み取りヘルパ ----

        private int ReadUInt16BE(int offset)
        {
            return (r_Bytes[offset] << 8) | r_Bytes[offset + 1];
        }

        private uint ReadUInt32BE(int offset)
        {
            return ((uint)r_Bytes[offset] << 24)
                | ((uint)r_Bytes[offset + 1] << 16)
                | ((uint)r_Bytes[offset + 2] << 8)
                | r_Bytes[offset + 3];
        }

        private long ReadVarint(ref int cursor)
        {
            return ReadVarint(r_Bytes, ref cursor);
        }

        // SQLiteの可変長整数（最大9バイト・ビッグエンディアン）
        private static long ReadVarint(byte[] data, ref int cursor)
        {
            var result = 0L;
            for (var i = 0; i < 8; i += 1) {
                var current = data[cursor];
                cursor += 1;
                result = (result << 7) | (current & 0x7FL);
                var hasMore = (current & 0x80) != 0;
                if (!hasMore) {
                    return result;
                }
            }
            // 9バイト目は全8ビットを使用する
            var ninth = data[cursor];
            cursor += 1;
            result = (result << 8) | ninth;
            return result;
        }
    }
}
