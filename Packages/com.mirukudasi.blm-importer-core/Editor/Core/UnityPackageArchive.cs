using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// unitypackage に収録されたアセット1件分の情報
    /// </summary>
    public sealed class UnityPackageAsset
    {
        public readonly string r_Guid;
        // 書庫に記録された更新時刻。作者の手元でそのファイルが最後に更新された時刻にあたる
        public readonly DateTime r_AuthorTimeUtc;
        // 書庫に記録された本体の大きさ。プロジェクト側のファイルと見比べて取り込み済みの版を推定するのに使う
        public readonly long r_Size;

        public UnityPackageAsset(string guid, DateTime authorTimeUtc, long size)
        {
            r_Guid = guid ?? "";
            r_AuthorTimeUtc = authorTimeUtc;
            r_Size = size;
        }
    }

    /// <summary>
    /// unitypackage は gzip 圧縮された tar 書庫で、収録アセット1件につき
    /// 「そのアセットのGUID」という名前のフォルダを持ち、その中の <c>asset</c> が本体にあたる。
    /// このクラスは本体を持つアセットのGUID・更新時刻・本体の大きさだけを取り出す。
    /// 中身は読み飛ばすため、プロジェクトへ展開せずに照合できる。
    /// Unity の API を呼ばないので、バックグラウンドスレッドから使える。
    /// </summary>
    public static class UnityPackageArchive
    {
        // tar は 512 バイト単位のブロックで構成され、各ファイルはヘッダ1ブロック＋本体が続く
        private const int c_BlockSize = 512;
        // ヘッダ内でのファイル名の位置と最大長
        private const int c_NameOffset = 0;
        private const int c_NameLength = 100;
        // ヘッダ内での本体サイズの位置と長さ。通常は8進数の文字列で入っている
        private const int c_SizeOffset = 124;
        private const int c_SizeLength = 12;
        // ヘッダ内での更新時刻の位置と長さ。8進数で、1970年からの経過秒
        private const int c_TimeOffset = 136;
        private const int c_TimeLength = 12;
        // GUID は32桁の16進数
        private const int c_GuidLength = 32;
        // アセット本体のエントリ名（"<guid>/asset"）
        private const string c_AssetEntryName = "asset";
        // 読み飛ばし用の作業バッファの大きさ
        private const int c_SkipBufferSize = c_BlockSize * 128;

        private static readonly DateTime r_UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// unitypackage に収録されたアセットの一覧を返す。
        /// 本体を持たないもの（フォルダなど）は含めない。
        /// ファイルが壊れていて途中で読めなくなった場合は、そこまでに読めた分を返す。
        /// </summary>
        public static IReadOnlyList<UnityPackageAsset> ReadAssets(string packagePath)
        {
            var assets = new List<UnityPackageAsset>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                using (var file = File.OpenRead(packagePath))
                using (var archive = new GZipStream(file, CompressionMode.Decompress))
                    CollectAssets(archive, assets, seen);
            } catch (Exception) {
                // 壊れたファイルや読み取り権限の問題で一覧表示を止めたくないため、読めた分だけ返す
            }
            return assets;
        }

        /// <summary>
        /// tar の各ヘッダを順に読み、アセット本体のエントリだけを拾う。
        /// 中身は使わないので読み飛ばす。
        /// </summary>
        private static void CollectAssets(Stream archive, List<UnityPackageAsset> assets, HashSet<string> seen)
        {
            var header = new byte[c_BlockSize];
            var skipBuffer = new byte[c_SkipBufferSize];
            // 空ブロックが2つ続いたら書庫の終端
            var emptyBlockCount = 0;
            while (ReadBlock(archive, header)) {
                var isEmptyBlock = IsAllZero(header);
                if (isEmptyBlock) {
                    emptyBlockCount += 1;
                    if (emptyBlockCount >= 2) {
                        return;
                    }
                }
                else {
                    emptyBlockCount = 0;
                    if (TryExtractAsset(header, out var asset) && seen.Add(asset.r_Guid)) {
                        assets.Add(asset);
                    }
                    Skip(archive, PaddedLength(ReadEntryLength(header)), skipBuffer);
                }
            }
        }

        /// <summary>
        /// ヘッダのファイル名が "&lt;32桁のGUID&gt;/asset" ならアセット本体とみなし、
        /// GUID・更新時刻・本体の大きさを取り出す。
        /// </summary>
        private static bool TryExtractAsset(byte[] header, out UnityPackageAsset asset)
        {
            asset = null;
            var name = ReadText(header, c_NameOffset, c_NameLength);
            if (name.StartsWith("./", StringComparison.Ordinal)) {
                name = name.Substring(2);
            }
            var segments = name.Split('/');
            var isAssetEntry = segments.Length == 2
                && IsGuid(segments[0])
                && string.Equals(segments[1], c_AssetEntryName, StringComparison.Ordinal);
            if (isAssetEntry) {
                asset = new UnityPackageAsset(segments[0], ReadEntryTimeUtc(header), ReadEntryLength(header));
                return true;
            }
            return false;
        }

        private static bool IsGuid(string text)
        {
            if (text.Length != c_GuidLength) {
                return false;
            }
            foreach (var character in text) {
                var isDigit = character >= '0' && character <= '9';
                var isLowerHex = character >= 'a' && character <= 'f';
                var isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex) {
                    return false;
                }
            }
            return true;
        }

        private static DateTime ReadEntryTimeUtc(byte[] header)
        {
            var seconds = ReadOctal(header, c_TimeOffset, c_TimeLength);
            return r_UnixEpochUtc.AddSeconds(seconds);
        }

        /// <summary>
        /// ヘッダから本体のバイト数を読む。
        /// 通常は8進数の文字列だが、非常に大きなファイルでは最上位ビットを立てた
        /// 256進数表現が使われるため、そちらにも対応する。
        /// </summary>
        private static long ReadEntryLength(byte[] header)
        {
            var isBase256 = (header[c_SizeOffset] & 0x80) != 0;
            if (isBase256) {
                return ReadBase256Length(header);
            }
            return ReadOctal(header, c_SizeOffset, c_SizeLength);
        }

        private static long ReadBase256Length(byte[] header)
        {
            // 最上位ビットは目印なので落とし、以降のバイトを上位から並べた整数として読む
            long value = header[c_SizeOffset] & 0x7F;
            for (var i = 1; i < c_SizeLength; i += 1) {
                value = (value << 8) | header[c_SizeOffset + i];
            }
            return value;
        }

        private static long ReadOctal(byte[] header, int offset, int length)
        {
            long value = 0;
            for (var i = 0; i < length; i += 1) {
                var character = (char)header[offset + i];
                var isOctalDigit = character >= '0' && character <= '7';
                if (isOctalDigit) {
                    value = value * 8 + (character - '0');
                }
            }
            return value;
        }

        // tar の本体は 512 バイト単位に詰め物をして格納される
        private static long PaddedLength(long length)
        {
            var remainder = length % c_BlockSize;
            if (remainder == 0) {
                return length;
            }
            return length + (c_BlockSize - remainder);
        }

        private static string ReadText(byte[] buffer, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && buffer[end] != 0) {
                end += 1;
            }
            return Encoding.UTF8.GetString(buffer, offset, end - offset);
        }

        private static bool IsAllZero(byte[] buffer)
        {
            foreach (var value in buffer) {
                if (value != 0) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>ブロック1つ分をきっちり読む。終端まで来ていたら false を返す。</summary>
        private static bool ReadBlock(Stream archive, byte[] block)
        {
            var filled = 0;
            while (filled < c_BlockSize) {
                var read = archive.Read(block, filled, c_BlockSize - filled);
                if (read <= 0) {
                    return false;
                }
                filled += read;
            }
            return true;
        }

        private static void Skip(Stream archive, long length, byte[] buffer)
        {
            var remaining = length;
            while (remaining > 0) {
                var request = (int)Math.Min(remaining, buffer.Length);
                var read = archive.Read(buffer, 0, request);
                if (read <= 0) {
                    return;
                }
                remaining -= read;
            }
        }
    }
}
