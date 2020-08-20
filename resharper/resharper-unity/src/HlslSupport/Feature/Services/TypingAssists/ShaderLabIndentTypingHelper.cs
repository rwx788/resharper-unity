using JetBrains.ReSharper.Feature.Services.TypingAssist;
using JetBrains.ReSharper.Plugins.Unity.ShaderLab.Psi;
using JetBrains.ReSharper.Psi.Cpp.Parsing;
using JetBrains.ReSharper.Psi.Impl.Shared.InjectedPsi;
using JetBrains.ReSharper.Psi.Parsing;

namespace JetBrains.ReSharper.Plugins.Unity.HlslSupport.Feature.Services.TypingAssists
{
    public class ShaderLabIndentTypingHelper : IndentTypingHelper<ShaderLabLanguage>
    {
        public ShaderLabIndentTypingHelper(TypingAssistLanguageBase<ShaderLabLanguage> assist)
            : base(assist)
        {
        }

        // smart backspaces expecteed that GetExtraStub return not null value, "foo " is typical valu
        protected override string GetExtraStub(CachingLexer lexer, int offset)
        {
            using (LexerStateCookie.Create(lexer))
            {
                lexer.FindTokenAt(offset);
                if (!(lexer.TokenType is CppTokenNodeType))
                    return "foo ";
            }
            return base.GetExtraStub(lexer, offset);
        }
    }
}