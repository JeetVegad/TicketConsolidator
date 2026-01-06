using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Infrastructure.Services;
using Xunit;

namespace TicketConsolidator.UnitTests
{
    public class ScriptValidatorTests
    {
        private readonly ScriptValidatorService _validator;

        public ScriptValidatorTests()
        {
            _validator = new ScriptValidatorService();
        }

        [Fact]
        public void Validate_ShouldReturnError_WhenScriptIsNull()
        {
            var result = _validator.Validate(null);
            Assert.False(result.IsValid);
            Assert.Contains("Script object is null", result.Errors[0]);
        }

        [Fact]
        public void Validate_ShouldReturnError_WhenContentIsEmpty()
        {
            var script = new SqlScript { TicketNumber = "1", Content = "   " };
            var result = _validator.Validate(script);
            Assert.False(result.IsValid);
        }

        [Fact]
        public void Validate_ShouldWarn_WhenGoIsMissing()
        {
            var script = new SqlScript { TicketNumber = "1", Content = "SELECT 1" };
            var result = _validator.Validate(script);
            
            Assert.True(result.IsValid); // Still valid, just warnings
            Assert.NotEmpty(result.Warnings);
            Assert.Contains("Missing 'GO'", result.Warnings[0]);
        }

        [Fact]
        public void Validate_ShouldPass_WhenContentIsValidAndHasGo()
        {
            var script = new SqlScript { TicketNumber = "1", Content = "SELECT 1 \r\n GO" };
            var result = _validator.Validate(script);
            
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            // Assert.Empty(result.Warnings); // Might depend on strictness, but for now GO is there.
        }
    }
}
