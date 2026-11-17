using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace WindowsDock;

internal static class IndexedDocumentText
{
    private const int PartLimit = 2 * 1024 * 1024;

    internal static string Read(string path, CancellationToken token) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => ReadPdf(path, token),
            ".xlsx" => ReadWorkbook(path, token),
            _ => ChatAttachments.Read(path).File.Text ?? ""
        };

    private static string ReadPdf(string path, CancellationToken token)
    {
        using var document = PdfDocument.Open(path);
        var text = new StringBuilder();
        for (var pageNumber = 1; pageNumber <= Math.Min(document.NumberOfPages, 100); pageNumber++)
        {
            token.ThrowIfCancellationRequested();
            var page = ContentOrderTextExtractor.GetText(document.GetPage(pageNumber)).Trim();
            if (page.Length == 0)
                continue;
            text.AppendLine($"Page {pageNumber}");
            if (Append(text, page))
                break;
        }
        return Bounded(text);
    }

    private static string ReadWorkbook(string path, CancellationToken token)
    {
        using var spreadsheet = ZipFile.OpenRead(path);
        var workbook = Part(spreadsheet, "xl/workbook.xml");
        var links = Part(spreadsheet, "xl/_rels/workbook.xml.rels");
        if (workbook == null || links == null)
            return "";
        var sharedPart = Part(spreadsheet, "xl/sharedStrings.xml");
        var shared = sharedPart?.Root?.Elements().Where(item => item.Name.LocalName == "si")
            .Take(20_000).Select(StringValue).ToArray() ?? [];
        var text = new StringBuilder();
        var workbookUri = new Uri("https://workbook.invalid/xl/workbook.xml");
        foreach (var sheet in workbook.Descendants().Where(item => item.Name.LocalName == "sheet").Take(24))
        {
            token.ThrowIfCancellationRequested();
            var id = sheet.Attributes().FirstOrDefault(item => item.Name.LocalName == "id")?.Value;
            var relationship = links.Root?.Elements().FirstOrDefault(item =>
                id != null && (string?)item.Attribute("Id") == id && (string?)item.Attribute("TargetMode") != "External");
            if (relationship?.Attribute("Target")?.Value is not { } target ||
                !Uri.TryCreate(workbookUri, target, out var part) || part.Host != workbookUri.Host ||
                !part.AbsolutePath.StartsWith("/xl/worksheets/", StringComparison.Ordinal))
                continue;
            var cells = Part(spreadsheet, Uri.UnescapeDataString(part.AbsolutePath.TrimStart('/')));
            if (cells == null)
                continue;
            text.AppendLine($"Sheet {(string?)sheet.Attribute("name") ?? "Unnamed"}");
            foreach (var row in cells.Descendants().Where(item => item.Name.LocalName == "row"))
            {
                token.ThrowIfCancellationRequested();
                var first = true;
                foreach (var cell in row.Elements().Where(item => item.Name.LocalName == "c"))
                {
                    var value = CellValue(cell, shared);
                    if (value.Length == 0)
                        continue;
                    if (!first)
                        text.Append(" | ");
                    first = false;
                    var remaining = Math.Max(0, ChatAttachments.TextLimit - text.Length);
                    text.Append(value, 0, Math.Min(remaining, value.Length));
                    if (text.Length >= ChatAttachments.TextLimit)
                        return Bounded(text);
                }
                if (!first)
                    text.AppendLine();
            }
        }
        return Bounded(text);
    }

    private static string CellValue(XElement cell, string[] shared)
    {
        if ((string?)cell.Attribute("t") == "inlineStr")
            return StringValue(cell);
        var value = cell.Elements().FirstOrDefault(item => item.Name.LocalName == "v")?.Value ?? "";
        if ((string?)cell.Attribute("t") == "s")
            return int.TryParse(value, out var index) && (uint)index < (uint)shared.Length ? shared[index] : "";
        return value;
    }

    private static string StringValue(XElement item) =>
        string.Concat(item.Descendants().Where(child => child.Name.LocalName == "t" &&
            !child.Ancestors().Any(parent => parent.Name.LocalName == "rPh")).Select(child => child.Value));

    private static XDocument? Part(ZipArchive file, string name)
    {
        var entry = file.GetEntry(name);
        if (entry == null)
            return null;
        if (entry.Length > PartLimit)
            throw new InvalidDataException("A workbook part exceeds the indexing limit.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = PartLimit
        });
        return XDocument.Load(reader);
    }

    private static bool Append(StringBuilder text, string value)
    {
        var remaining = ChatAttachments.TextLimit - text.Length;
        if (remaining <= 0)
            return true;
        if (value.Length == 0)
            return false;
        text.Append(value, 0, Math.Min(remaining, value.Length)).AppendLine();
        return text.Length >= ChatAttachments.TextLimit;
    }

    private static string Bounded(StringBuilder text)
    {
        var length = Math.Min(text.Length, ChatAttachments.TextLimit);
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text.ToString(0, length).Trim();
    }
}
