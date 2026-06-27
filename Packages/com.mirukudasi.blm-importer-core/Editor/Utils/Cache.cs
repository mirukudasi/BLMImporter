using System;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// 遅延シングルトン。初回アクセス時に factory で生成して以後は使い回す。
    /// ClearCache で破棄すると次回アクセスで作り直す。factory が例外を投げた場合はキャッシュしない。
    /// </summary>
    public sealed class Cache<T>
    {
        private readonly Func<T> r_Factory;
        private bool m_HasValue = false;
        private T m_Value = default;

        public Cache(Func<T> factory)
        {
            r_Factory = factory;
        }

        public T Value {
            get {
                if (!m_HasValue) {
                    // 生成成功後にだけフラグを立てる。例外時は未キャッシュのままにして次回再試行できるようにする
                    m_Value = r_Factory();
                    m_HasValue = true;
                }
                return m_Value;
            }
        }

        public void ClearCache()
        {
            m_HasValue = false;
            m_Value = default;
        }
    }
}
