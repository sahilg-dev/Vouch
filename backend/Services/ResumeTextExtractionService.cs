using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace JobCopilot.Api.Services;

public class ResumeTextExtractionException(string message) : Exception(message);

public class ResumeTextExtractionService
{
    public async Task<string> ExtractAsync(Stream file, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var text = ext switch
        {
            ".pdf" => ExtractPdf(file),
            ".docx" => ExtractDocx(file),
            ".txt" or ".md" => await new StreamReader(file).ReadToEndAsync(),
            _ => throw new ResumeTextExtractionException("Unsupported file type. Upload a .pdf, .docx, .txt, or .md resume.")
        };

        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 80)
            throw new ResumeTextExtractionException(
                "Couldn't find enough text in that file — paste at least a few lines of resume text instead.");

        return text;
    }

    // page.Text concatenates every word on the page in reading order with no line
    // breaks, which mangles a resume's structure. Group words into visual lines by
    // vertical position instead, so headings/bullets survive as separate lines.
    static string ExtractPdf(Stream file)
    {
        using var doc = PdfDocument.Open(file);
        var pageTexts = doc.GetPages().Select(page =>
        {
            var lines = page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
            return string.Join("\n", lines);
        });
        return string.Join("\n\n", pageTexts);
    }

    static string ExtractDocx(Stream file)
    {
        using var doc = WordprocessingDocument.Open(file, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        // Join per-paragraph so line structure survives roughly intact, rather than
        // running every run of text together into one unbroken line.
        return string.Join("\n", body.Descendants<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)))
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
