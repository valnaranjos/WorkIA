using KommoAIAgent.Models;
using KommoAIAgent.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;
using KommoAIAgent.Helpers;

namespace KommoAIAgent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KommoWebhookController : ControllerBase
{
    private readonly IWebhookHandler _webhookHandler;
    private readonly ILogger<KommoWebhookController> _logger;


    public KommoWebhookController(IWebhookHandler webhookHandler, ILogger<KommoWebhookController> logger)
    {
        _webhookHandler = webhookHandler;
        _logger = logger;
    }

    /// <summary>
    /// Endpoint para recibir el webhook entrantes de Kommo.
    /// </summary>
    /// <returns></returns>
    [HttpPost("incoming")]
    public async Task<IActionResult> HandleIncomingMessage()
    {
        _logger.LogInformation("--- Webhook de Kommo (form-urlencoded) recibido ---");
        try
        {
            //Inicializamos el payload vacío.
            var payload = new KommoWebhookPayload
            {
                Message = new MessageData { AddedMessages = [] }
            };

            //  FORM x-www-form-urlencoded
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                _logger.LogDebug("Kommo form keys: {Keys}", string.Join(", ", form.Keys));

                long? leadId = null;
                if (long.TryParse(form["message[add][0][entity_id]"], out var lid)) leadId = lid;
                else if (long.TryParse(form["lead_id"], out var lid2)) leadId = lid2;

                if (leadId is null)
                {
                    _logger.LogWarning("Webhook recibido pero no contenía un mensaje válido con LeadId. Se ignora.");
                    return Ok();
                }

                // Extrae el mensaje
                string? text = Utils.FirstNonEmpty(
                    form["message[add][0][text]"].ToString(),
                    form["text"].ToString(),
                    form["message[text]"].ToString()
                );


                // Crea el objeto MessageDetails con los campos.
                var md = new MessageDetails
                {
                    MessageId = Utils.FirstNonEmpty(form["message[add][0][id]"].ToString()),
                    Type = Utils.FirstNonEmpty(form["message[add][0][type]"].ToString()),
                    Text = text,
                    ChatId = Utils.FirstNonEmpty(form["message[add][0][chat_id]"].ToString()),
                    EntityType = Utils.FirstNonEmpty(form["message[add][0][entity_type]"].ToString()),
                    LeadId = leadId
                };

                // Singular
                var attachLink = Utils.FirstNonEmpty(
                    form["message[add][0][attachment][link]"].ToString(),
                    form["message[add][0][attachment][url]"].ToString(),
                    form["message[add][0][attachment][download_link]"].ToString()
                );
                if (!string.IsNullOrWhiteSpace(attachLink))
                {
                    //Crea el attachment con las variables que necesitamos.
                    var att = new AttachmentInfo
                    {
                        Url = attachLink,
                        Type = Utils.FirstNonEmpty(form["message[add][0][attachment][type]"].ToString()),
                        Name = Utils.FirstNonEmpty(form["message[add][0][attachment][file_name]"].ToString(),
                                                 form["message[add][0][attachment][name]"].ToString()),
                        MimeType = Utils.FirstNonEmpty(form["message[add][0][attachment][mime_type]"].ToString(),
                                                 form["message[add][0][attachment][content_type]"].ToString())
                    };
                    // si type parece MIME
                    if (string.IsNullOrWhiteSpace(att.MimeType) &&
                        !string.IsNullOrWhiteSpace(att.Type) &&
                        att.Type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        att.MimeType = att.Type;

                    md.Attachments.Add(att);
                    _logger.LogInformation("Adjunto (singular) encontrado: {Url} (type={Type}, mime={Mime})",
                       Utils.MaskUrl(att.Url), att.Type, att.MimeType);
                }

                //PLURAL: message[add][0][attachments][i][...]
                foreach (var att in ExtractAttachmentsPlural(form))
                {
                    md.Attachments.Add(att);
                    _logger.LogInformation("Adjunto (plural) encontrado: {Url} (type={Type}, mime={Mime})",
                       Utils.MaskUrl(att.Url), att.Type, att.MimeType);
                }

                payload.Message.AddedMessages.Add(md);
            }
            else
            {
                // JSON (por si Kommo lo envía así en integración)
                using var reader = new StreamReader(Request.Body);
                var jsonStr = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(jsonStr))
                {
                    var md = ExtractFromJson(jsonStr, _logger);
                    if (md is null)
                    {
                        _logger.LogWarning("JSON recibido pero no se pudo extraer LeadId/mensaje.");
                        return Ok();
                    }

                    payload.Message.AddedMessages.Add(md);
                }
                else
                {
                    _logger.LogWarning("Webhook vacío. Se ignora.");
                    return Ok();
                }
            }

            // Fire-and-forget
            _ = _webhookHandler.ProcessIncomingMessageAsync(payload);
            return Ok("Webhook received.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al leer o procesar el webhook de Kommo.");
            return BadRequest("Could not process webhook.");
        }
    }


   
  

    // Lógica para extraer attachments de la forma PLURAL: message[add][0][attachments][i][...]
    private static IEnumerable<AttachmentInfo> ExtractAttachmentsPlural(IFormCollection form)
    {
        var map = new Dictionary<int, AttachmentInfo>();
        var rx = new Regex(@"attachments\[(\d+)\].*\[(\w+)\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var key in form.Keys)
        {
            if (!key.Contains("attachments", StringComparison.OrdinalIgnoreCase)) continue;
            var m = rx.Match(key);
            if (!m.Success) continue;

            int i = int.Parse(m.Groups[1].Value);
            string field = m.Groups[2].Value.ToLowerInvariant();
            string value = form[key].ToString();

            if (!map.TryGetValue(i, out var att))
            {
                att = new AttachmentInfo();
                map[i] = att;
            }

            switch (field)
            {
                case "url":
                case "file":
                case "download_link":
                    if (string.IsNullOrWhiteSpace(att.Url)) att.Url = value;
                    break;

                case "name":
                case "filename":
                    att.Name = value;
                    break;

                case "mime_type":
                    att.MimeType = value;
                    break;

                case "type":
                case "file_type":
                    att.Type = value;
                    if (string.IsNullOrWhiteSpace(att.MimeType) &&
                        value.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        att.MimeType = value;
                    break;

                case "content_type":
                    if (string.IsNullOrWhiteSpace(att.MimeType)) att.MimeType = value;
                    else if (string.IsNullOrWhiteSpace(att.Type)) att.Type = value;
                    break;
            }
        }

        //Para cada attachment válido (con URL), lo devolvemos.
        foreach (var kv in map.OrderBy(k => k.Key))
        {
            var a = kv.Value;
            if (!string.IsNullOrWhiteSpace(a.Url))
                yield return a;
        }
    }

    // Lógica que extrae desde JSON
    private static MessageDetails? ExtractFromJson(string json, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // path: message.add[0]
            if (!root.TryGetProperty("message", out var msg)) return null;
            var add = msg.GetProperty("add")[0];

            var entityId = add.GetProperty("entity_id").GetInt64();
            var text = add.TryGetProperty("text", out var t) ? t.GetString() : null;
            var msgId = add.TryGetProperty("id", out var mid) ? mid.ToString() : null;
            var chatId = add.TryGetProperty("chat_id", out var ch) ? ch.ToString() : null;
            var entityTyp = add.TryGetProperty("entity_type", out var et) ? et.GetString() : null;

            var md = new MessageDetails
            {
                MessageId = msgId,
                Type = add.TryGetProperty("type", out var tp) ? tp.GetString() : null,
                Text = text,
                ChatId = chatId,
                EntityType = entityTyp,
                LeadId = entityId
            };

            if (add.TryGetProperty("attachment", out var att))
            {
                var a = new AttachmentInfo
                {
                    Url = Utils.FirstNonEmpty(att.TryGetProperty("link", out var l) ? l.GetString() : null,
                                             att.TryGetProperty("url", out var u) ? u.GetString() : null,
                                             att.TryGetProperty("download_link", out var d) ? d.GetString() : null),
                    Type = att.TryGetProperty("type", out var ty) ? ty.GetString() : null,
                    Name = Utils.FirstNonEmpty(att.TryGetProperty("file_name", out var fn) ? fn.GetString() : null,
                                             att.TryGetProperty("name", out var nm) ? nm.GetString() : null),
                    MimeType = Utils.FirstNonEmpty(att.TryGetProperty("mime_type", out var mm) ? mm.GetString() : null,
                                             att.TryGetProperty("content_type", out var ct) ? ct.GetString() : null)
                };
                if (!string.IsNullOrWhiteSpace(a.Url)) md.Attachments.Add(a);
            }

            return md;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo parsear JSON de Kommo.");
            return null;
        }
    }   
}