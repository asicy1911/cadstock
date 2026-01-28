using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        private bool _hooked;

        public void Initialize()
        {
            // ✅ 不等 Idle，直接挂上更新事件，避免“面板不刷新”
            TryHook();
        }

        public void Terminate()
        {
            try
            {
                if (_hooked)
                {
                    StockQuoteService.Instance.DataUpdated -= OnUpdated;
                    _hooked = false;
                }
            }
            catch { }

            try { StockQuoteService.Instance.Stop(); } catch { }
            try { PaletteHost.DisposePalette(); } catch { }
        }

        private void TryHook()
        {
            if (_hooked) return;
            _hooked = true;

            try
            {
                StockQuoteService.Instance.DataUpdated += OnUpdated;
            }
            catch { }
        }

        private static void OnUpdated()
        {
            // ✅ 统一走 PaletteHost（内部会处理跨线程刷新）
            try { PaletteHost.NotifyQuotesUpdated(); } catch { }
        }
    }
}
