using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Mikeka.Api.Services;

public interface INotifier
{
    Task SendAsync(string subject, string body, CancellationToken ct);
}

/// <summary>Sends alerts by SMTP (Gmail: use an App Password). Without SMTP settings it only logs.</summary>
public class EmailService(IOptions<EmailOptions> options, ILogger<EmailService> log) : INotifier
{
    public async Task SendAsync(string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.User) || string.IsNullOrWhiteSpace(o.Password))
        {
            log.LogWarning("Email not configured; would send to {To}: {Subject}\n{Body}", o.To, subject, body);
            return;
        }

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(o.From ?? o.User));
        msg.To.Add(MailboxAddress.Parse(o.To));
        msg.Subject = "[Mikeka] " + subject;
        msg.Body = new TextPart("plain") { Text = body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.Host, o.Port, o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(o.User, o.Password, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
        log.LogInformation("Email sent: {Subject}", subject);
    }
}
