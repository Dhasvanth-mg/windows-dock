using System.IO;
using System.IO.Compression;
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
    public async Task FindsPdfPagesAndSpreadsheetTextInsideUploadedDocuments()
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

        Assert.True(await DockFileLibrary.UploadAsync(Path.Combine(folder, "report.pdf"), CancellationToken.None));
        Assert.True(await DockFileLibrary.UploadAsync(Path.Combine(folder, "budget.xlsx"), CancellationToken.None));
        var report = Assert.Single(DockFileLibrary.Search("orchard", CancellationToken.None));
        Assert.Equal("report.pdf", report.Name);
        Assert.Contains("Quarterly orchard findings", DockFileLibrary.Preview(report.Id)!.File.Text);
        var workbookResult = Assert.Single(DockFileLibrary.Search("conference rail 42", CancellationToken.None));
        Assert.Equal("budget.xlsx", workbookResult.Name);
        Assert.Contains("Sheet Events", DockFileLibrary.Preview(workbookResult.Id)!.File.Text);
        Assert.DoesNotContain(folder, report.Snippet);
    }

    private static void Part(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(text);
    }

    [Fact]
    public async Task UploadedImagesHaveSearchableVisualPropertiesAndStayPreviewable()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "pictures")).FullName;
        var image = Path.Combine(folder, "sample.png");
        using (var bitmap = new System.Drawing.Bitmap(820, 280))
        using (var canvas = System.Drawing.Graphics.FromImage(bitmap))
        using (var font = new System.Drawing.Font("Segoe UI", 56))
        {
            canvas.Clear(System.Drawing.Color.MidnightBlue);
            canvas.DrawString("COPPER PELICAN", font, System.Drawing.Brushes.White, 30, 100);
            bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
        }
        Assert.True(await DockFileLibrary.UploadAsync(image, CancellationToken.None));
        var match = Assert.Single(DockFileLibrary.Search("blue landscape", CancellationToken.None));
        var preview = DockFileLibrary.Preview(match.Id)!;
        Assert.True(preview.File.Image);
        Assert.Equal(File.ReadAllBytes(image), preview.Image);
        Assert.NotNull(await LibraryImageAnalysis.PreviewAsync(preview.Image!, CancellationToken.None));
        if (!preview.File.Analysis.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            Assert.Single(DockFileLibrary.Search("copper pelican", CancellationToken.None));
    }
}
