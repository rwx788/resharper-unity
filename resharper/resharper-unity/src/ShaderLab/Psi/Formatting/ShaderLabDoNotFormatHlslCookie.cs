using System;
using JetBrains.Diagnostics;

namespace JetBrains.ReSharper.Plugins.Unity.ShaderLab.Psi.Formatting
{
    public class ShaderLabDoNotFormatHlslCookie : IDisposable
    {
        [ThreadStatic] public static bool IsHlslFormatterSuppressed;

        public ShaderLabDoNotFormatHlslCookie()
        {
            Assertion.Assert(!IsHlslFormatterSuppressed, "!IsHlslFormatterSuppressed, Reentrancy is not expected");
            IsHlslFormatterSuppressed = true;
            
        }

        public void Dispose()
        {
            IsHlslFormatterSuppressed = false;
        }
    }
}