namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// data.db の1行から作る Master の共通の約束
    /// 読み込む条件を満たさない行は <see cref="LibraryMaster"/> が読み飛ばす
    /// </summary>
    internal interface ILibraryMasterRow
    {
        bool IsValid { get; }
    }
}
