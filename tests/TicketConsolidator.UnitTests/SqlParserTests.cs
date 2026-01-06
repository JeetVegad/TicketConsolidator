using System.Collections.Generic;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Infrastructure.Services;
using Xunit;

namespace TicketConsolidator.UnitTests
{
    public class SqlParserTests
    {
        private readonly SqlParserService _parser;

        public SqlParserTests()
        {
            _parser = new SqlParserService();
        }

        [Fact]
        public void ParseScript_ShouldExtractContent_WhenTagsArePresent()
        {
            // Arrange
            string ticket = "TICK-123";
            string content = @"
Some Header Info
<TICK-123> START
SELECT * FROM Table
<TICK-123> END
Footer info
            ";

            // Act
            var result = _parser.ParseScript(content, "file.sql", ticket);

            // Assert
            Assert.Single(result);
            Assert.Equal("SELECT * FROM Table", result[0].Content);
            Assert.Equal(ticket, result[0].TicketNumber);
        }

        [Fact]
        public void ParseScript_ShouldExtractMultipleBlocks_WhenTagsRepeat()
        {
            // Arrange
            string ticket = "TICK-123";
            string content = @"
<TICK-123> START
Proc 1
<TICK-123> END

Some junk

<TICK-123> START
Proc 2
<TICK-123> END
            ";

            // Act
            var result = _parser.ParseScript(content, "file.sql", ticket);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("Proc 1", result[0].Content);
            Assert.Equal("Proc 2", result[1].Content);
        }

        [Fact]
        public void ParseScript_ShouldDetectStoredProcedure_FromContent()
        {
            // Arrange
            string content = "<T-1> START CREATE PROCEDURE dbo.Test AS BEGIN END <T-1> END";

            // Act
            var result = _parser.ParseScript(content, "unknown.sql", "T-1");

            // Assert
            Assert.Single(result);
            Assert.Equal(ScriptType.StoredProcedure, result[0].Type);
        }

        [Fact]
        public void ParseScript_ShouldDetectDataScript_FromFileName()
        {
             // Arrange
            string content = "<T-1> START INSERT INTO Tbl (A) VALUES (1) <T-1> END";

            // Act
            var result = _parser.ParseScript(content, "MyScript_Data.sql", "T-1");

            // Assert
            Assert.Single(result);
            Assert.Equal(ScriptType.Data, result[0].Type);
        }
    }
}
