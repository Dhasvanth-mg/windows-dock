using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DocumentRetrievalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-index-" + Guid.NewGuid().ToString("N"));

    public DocumentRetrievalTests() => DockData.RootOverride = Path.Combine(root, "data");

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void FindsPdfPagesAndSpreadsheetTextAfterAnIndexVersionUpgrade()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "documents")).FullName;
        var pdf = new PdfDocumentBuilder();
        var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
        var page = pdf.AddPage(PageSize.A4);
        page.AddText("Quarterly orchard findings", 12, new PdfPoint(40, 700), font);
        File.WriteAllBytes(Path.Combine(folder, "report.pdf"), pdf.Build());
        using (var workbook = ZipFile.Open(Path.Combine(folder, "budget.xlsx"), ZipArchiveMode.Create))
        {
            Part(workbook, "xl/workbook.xml", """<workbook xmlns:r="urn:relationship"><sheets><sheet name="Events" r:id="rId1"/></sheets></workbook>""");
            Part(workbook, "xl/_rels/workbook.xml.rels", """<Relationships><Relationship Id="rId1" Target="worksheets/sheet2.xml"/></Relationships>""");
            Part(workbook, "xl/sharedStrings.xml", """<sst><si><t>Conference travel</t></si></sst>""");
            Part(workbook, "xl/worksheets/sheet2.xml", """
                <worksheet><sheetData><row><c t="s"><v>0</v></c><c t="inlineStr"><is><t>Rail fare</t></is></c>
                <c><f>40+2</f><v>42</v></c></row></sheetData></worksheet>
                """);
        }

        DockFileSearch.AddFolder(folder);
        DockFileSearch.Update(folder, CancellationToken.None);
        Assert.Equal("report.pdf", Assert.Single(DockFileSearch.Search("orchard")).Name);
        Assert.Equal("budget.xlsx", Assert.Single(DockFileSearch.Search("conference rail 42")).Name);
        using (var connection = new SqliteConnection("Data Source=" + DockData.FilePath("file-search.db") + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE document_text SET content = ''; PRAGMA user_version = 0;";
            command.ExecuteNonQuery();
        }
        Assert.Empty(DockFileSearch.Search("orchard"));
        DockFileSearch.Update(folder, CancellationToken.None);
        var result = Assert.Single(DockFileSearch.Search("orchard"));
        var excerpt = DockFileSearch.ExcerptText([result]);
        Assert.Contains("report.pdf", excerpt);
        Assert.Contains("Quarterly orchard findings", excerpt);
        Assert.DoesNotContain(folder, excerpt);
    }

    private static void Part(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(text);
    }
}
