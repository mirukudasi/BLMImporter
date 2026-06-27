namespace BLMImporter.Editor.Core
{
    /// <summary>URL文字列を正規化して持つ値型。空なら無効。</summary>
    public readonly struct Url
    {
        public readonly string r_Value;

        public Url(string value)
        {
            r_Value = (value ?? "").Trim();
        }

        public bool IsValid => !string.IsNullOrEmpty(r_Value);

        public override string ToString() => r_Value;
    }
}
