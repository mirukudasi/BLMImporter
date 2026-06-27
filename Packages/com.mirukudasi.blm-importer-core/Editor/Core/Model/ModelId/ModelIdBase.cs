using System;
using System.Collections.Generic;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// 各モデルのIDの基底。具象型＋値の両方が一致するときのみ等価とみなすため、
    /// ItemId と別モデルのIDが同じ値でも混同されない。
    /// </summary>
    public abstract class ModelIdBase : IEquatable<ModelIdBase>
    {
        // 等価判定・ハッシュに使う値（数値IDなら long、文字列IDなら正規化済み string）
        protected abstract object ComparableValue { get; }
        public abstract bool IsValid { get; }

        public bool Equals(ModelIdBase other)
        {
            if (other == null) {
                return false;
            }
            return GetType() == other.GetType() && Equals(ComparableValue, other.ComparableValue);
        }

        public override bool Equals(object obj) => Equals(obj as ModelIdBase);

        public override int GetHashCode()
        {
            var value = ComparableValue;
            var valueHash = value == null ? 0 : value.GetHashCode();
            return (GetType().GetHashCode() * 397) ^ valueHash;
        }

        public override string ToString()
        {
            var value = ComparableValue;
            return value == null ? "" : value.ToString();
        }
    }

    /// <summary>値の型を指定するIDの基底。</summary>
    public abstract class ModelIdBase<T> : ModelIdBase
    {
        public readonly T r_Value;

        protected ModelIdBase(T value)
        {
            r_Value = value;
        }

        protected override object ComparableValue => r_Value;
    }
}
