using System;
using System.Threading;

namespace Fort.ind_UWP
{
    public sealed class Debouncer
    {
        private CancellationTokenSource _cts;

        public CancellationToken Restart()
        {
            Cancel();

            var cts = new CancellationTokenSource();
            _cts = cts;
            return cts.Token;
        }

        public void Cancel()
        {
            var cts = _cts;
            _cts = null;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            cts.Dispose();
        }
    }
}
