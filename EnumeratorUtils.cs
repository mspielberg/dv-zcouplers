using System;
using System.Collections;

namespace DvMod.ZCouplers
{
    public class EnumeratorWrapper : IEnumerator
    {
        private readonly IEnumerator inner;
        private readonly Action postfix;

        public EnumeratorWrapper(IEnumerator inner, Action postfix)
        {
            this.inner = inner;
            this.postfix = postfix;
        }

        public object Current => inner.Current;

        public bool MoveNext()
        {
            var more = inner.MoveNext();
            if (more)
                return true;
            postfix();
            return false;
        }

        public void Reset()
        {
            inner.Reset();
        }
    }
}