using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PdfDocument = QuestPDF.Fluent.Document;
using PdfFonts = QuestPDF.Helpers.Fonts;

namespace JobCopilot.Api.Services;

// PDF via QuestPDF (Community license — free under its revenue threshold, see
// questpdf.com/license; re-check before a real commercial launch). DOCX via
// DocumentFormat.OpenXml (Microsoft, MIT — no licensing concern).
public class ResumeExportService
{
    static ResumeExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] ToPdf(Candidate candidate, JobPosting job, TailoredContent content, string coverNote)
    {
        using var stream = new MemoryStream();
        PdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10.5f).FontFamily(PdfFonts.Calibri));

                page.Header().Column(col =>
                {
                    col.Item().Text(candidate.FullName).FontSize(20).Bold();
                    col.Item().Text($"{candidate.Email}{(string.IsNullOrWhiteSpace(candidate.Phone) ? "" : " · " + candidate.Phone)}{(string.IsNullOrWhiteSpace(candidate.Location) ? "" : " · " + candidate.Location)}")
                        .FontSize(9.5f).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(2).Text($"Tailored for {job.Title} · {job.Company}").FontSize(9.5f).Italic();
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(content.Summary).FontSize(10.5f);

                    foreach (var section in content.Sections)
                    {
                        col.Item().PaddingTop(6).Text(section.Heading.ToUpperInvariant())
                            .FontSize(11).Bold().FontColor(Colors.Green.Darken2);
                        foreach (var bullet in section.Bullets)
                            col.Item().Row(row =>
                            {
                                row.ConstantItem(12).Text("•");
                                row.RelativeItem().Text(bullet.Text);
                            });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Prepared by Vouch — every line traces to a fact in the candidate's ledger.")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(stream);
        return stream.ToArray();
    }

    public byte[] ToDocx(Candidate candidate, JobPosting job, TailoredContent content, string coverNote)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(Heading(candidate.FullName, 32));
            body.AppendChild(Paragraph($"{candidate.Email}{(string.IsNullOrWhiteSpace(candidate.Phone) ? "" : " · " + candidate.Phone)}{(string.IsNullOrWhiteSpace(candidate.Location) ? "" : " · " + candidate.Location)}", italic: false, size: 18));
            body.AppendChild(Paragraph($"Tailored for {job.Title} · {job.Company}", italic: true, size: 18));
            body.AppendChild(Paragraph(""));
            body.AppendChild(Paragraph(content.Summary));

            foreach (var section in content.Sections)
            {
                body.AppendChild(Heading(section.Heading.ToUpperInvariant(), 24));
                foreach (var bullet in section.Bullets)
                    body.AppendChild(Paragraph($"• {bullet.Text}"));
            }
        }
        return stream.ToArray();
    }

    static DocumentFormat.OpenXml.Wordprocessing.Paragraph Heading(string text, int size) =>
        new(new Run(
            new RunProperties(new Bold(), new FontSize { Val = size.ToString() }),
            new Text(text)));

    static DocumentFormat.OpenXml.Wordprocessing.Paragraph Paragraph(string text, bool italic = false, int size = 20)
    {
        var props = new RunProperties(new FontSize { Val = size.ToString() });
        if (italic) props.Append(new Italic());
        return new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new Run(props, new Text(text)));
    }
}
