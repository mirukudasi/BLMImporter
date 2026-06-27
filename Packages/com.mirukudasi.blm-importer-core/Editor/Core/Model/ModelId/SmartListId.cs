namespace BLMImporter.Editor.Core
{
    /// <summary>スマートリストのID（smart_lists.id）。</summary>
    public sealed class SmartListId : ModelIdBase<long>
    {
        public SmartListId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
