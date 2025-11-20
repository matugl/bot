using DirectLineMiddleware.Interfaces;
using DirectLineMiddleware.Transcript;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class ProxyBot : ActivityHandler
{
    private readonly IExternalBotClient _external;
    private readonly IOmnichannelService _omnichannel;

    public ProxyBot(IExternalBotClient external, IOmnichannelService omnichannel)
    {
        _external = external;
        _omnichannel = omnichannel;
    }

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;

        // Guardar transcript siempre
        SaveToTranscript(activity as Activity);

        // Detectar si es un mensaje del agente
        var isAgentMessage =
            activity.ChannelId == "omnichannel" &&
            activity.From?.Role == "agent";

        if (isAgentMessage)
        {
            await HandleAgentMessage(turnContext, cancellationToken);
            return;
        }

        // Mensaje del cliente (DirectLine / Santex)
        await HandleCustomerMessage(turnContext, cancellationToken);
    }


    // ======================================================
    //  CLIENTE → BOT EXTERNO → CLIENTE
    // ======================================================
    private async Task HandleCustomerMessage(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;

        var message = activity.Text;
        var userId = activity.From?.Id ?? "anonymous";
        var convId = activity.Conversation?.Id ?? "no-conv";

        string externalReply;

        try
        {
            externalReply = await _external.SendAsync(message, userId, convId);

            if (string.IsNullOrWhiteSpace(externalReply))
                externalReply = "(sin respuesta del bot externo)";
        }
        catch (Exception)
        {
            externalReply = "Tuvimos un problema hablando con el bot externo.";
        }

        var reply = MessageFactory.Text(externalReply);

        // Setear identidad de bot
        reply.From = new ChannelAccount("mw-bot", "Middleware Bot");
        reply.Conversation = activity.Conversation;

        SaveToTranscript(reply as Activity);

        await turnContext.SendActivityAsync(reply, cancellationToken);
    }



    // ======================================================
    //  AGENTE → SANTEX
    // ======================================================
    private async Task HandleAgentMessage(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var text = turnContext.Activity.Text;
        var ocConvId = turnContext.Activity.Conversation.Id;

        // Recuperar mapping
        if (!ConversationMapStore.TryGet(ocConvId, out var map))
        {
            await turnContext.SendActivityAsync(
                "No se encontró la sesión del cliente para reenviar el mensaje.",
                cancellationToken: cancellationToken);

            return;
        }

        // Enviar mensaje desde el agente hacia Santex
        await _external.SendAsync(
            text,
            map.ClientUserId,                // userId real del cliente
            map.DirectLineConversationId     // conversación de Direct Line
        );
    }



    // ======================================================
    //  ESCALATE → CREAR SESIÓN OC
    // ======================================================
    public override async Task OnTurnAsync(
        ITurnContext turnContext,
        CancellationToken cancellationToken = default)
    {
        var auth = turnContext.Activity.ServiceUrl;
        Console.WriteLine("Headers: " + JsonConvert.SerializeObject(turnContext.Activity));
        // Guardar todo en transcript
        SaveToTranscript(turnContext.Activity as Activity);

        if (turnContext.Activity.Type == ActivityTypes.Event &&
            turnContext.Activity.Name == "escalate")
        {
            var convId = turnContext.Activity.Conversation.Id;

            // 1. Recuperar transcript previo
            var transcript = TranscriptStore.Store[convId].Activities;

            // 2. Crear sesión OC
            var sessionId = await _omnichannel.CreateSessionAsync(turnContext.Activity);

            // 3. Guardar el mapping OC ↔ DirectLine
            ConversationMapStore.Save(new ConversationMap
            {
                OcConversationId = sessionId,
                DirectLineConversationId = convId,
                ClientUserId = turnContext.Activity.From.Id
            });

            // 4. Enviar transcript
            await _omnichannel.SendTranscriptAsync(sessionId, transcript);

            // 5. Handoff
            await _omnichannel.TriggerHandoffAsync(sessionId, "Escalado desde bot externo Santex");

            // 6. Avisar a Santex
            await turnContext.SendActivityAsync("Escalate procesado y sesión creada en Omnichannel.");

            return;
        }

        await base.OnTurnAsync(turnContext, cancellationToken);
    }



    // ======================================================
    //  TRANSCRIPT
    // ======================================================
    private void SaveToTranscript(Activity activity)
    {
        if (activity == null) return;

        var convId = activity.Conversation.Id;

        if (!TranscriptStore.Store.ContainsKey(convId))
        {
            TranscriptStore.Store[convId] = new TranscriptRecord
            {
                ConversationId = convId
            };
        }

        var clone = activity.CloneActivity();
        clone.Timestamp = DateTime.UtcNow;

        TranscriptStore.Store[convId].Activities.Add(clone);
    }
}






//using DirectLineMiddleware.Interfaces;
//using DirectLineMiddleware.Transcript;
//using Microsoft.Bot.Builder;
//using Microsoft.Bot.Builder.Integration.AspNet.Core;
//using Microsoft.Bot.Configuration;
//using Microsoft.Bot.Schema;

//public class ProxyBot : ActivityHandler
//{
//    private readonly IExternalBotClient _external;
//    private readonly IOmnichannelService _omnichannel;

//    public ProxyBot(IExternalBotClient external, IOmnichannelService omnichannel)
//    {
//        _external = external;
//        _omnichannel = omnichannel;
//    }

//    protected override async Task OnMessageActivityAsync(
//    ITurnContext<IMessageActivity> turnContext,
//    CancellationToken cancellationToken)
//    {
//        var activity = turnContext.Activity;

//        // Guardamos lo que mandó el usuario (Santex/cliente)
//        SaveToTranscript(activity as Activity);

//        var userMessage = activity.Text;
//        var userId = activity.From?.Id ?? "anonymous";
//        var conversationId = activity.Conversation?.Id ?? "no-conv";

//        string externalReply;

//        try
//        {
//            // 1) Mandar mensaje al bot externo (python de Santex o lo que sea)
//            externalReply = await _external.SendAsync(userMessage, userId, conversationId);
//            if (string.IsNullOrWhiteSpace(externalReply))
//                externalReply = "(sin respuesta del bot externo)";
//        }
//        catch (Exception ex)
//        {
//            // Loguear y responder algo digno
//            externalReply = "Tuvimos un problema hablando con el bot externo.";
//        }

//        // 2) Responder al cliente a través de Direct Line
//        var reply = MessageFactory.Text(externalReply);

//        // lo marcamos como "bot" para el transcript
//        reply.From = new ChannelAccount("mw-bot", "Middleware Bot");
//        reply.Conversation = activity.Conversation;

//        SaveToTranscript(reply as Activity);

//        await turnContext.SendActivityAsync(reply, cancellationToken);
//    }



//    public override async Task OnTurnAsync(
//    ITurnContext turnContext,
//    CancellationToken cancellationToken = default)
//    {
//        // Guardamos toda activity en transcript
//        SaveToTranscript(turnContext.Activity as Activity);

//        if (turnContext.Activity.Type == ActivityTypes.Event &&
//            turnContext.Activity.Name == "escalate")
//        {
//            var convId = turnContext.Activity.Conversation.Id;

//            // 1. Recuperar transcript interno
//            var transcript = TranscriptStore.Store[convId].Activities;

//            // 2. Crear sesión OC
//            var sessionId = await _omnichannel.CreateSessionAsync(turnContext.Activity);

//            // 3. Enviar transcript histórico
//            await _omnichannel.SendTranscriptAsync(sessionId, transcript);

//            // 4. Notificar handoff al agente
//            await _omnichannel.TriggerHandoffAsync(sessionId, "Escalado desde bot externo Santex");

//            // 5. Confirmar a Santex
//            await turnContext.SendActivityAsync("Escalate procesado, sesión creada en Omnichannel.");

//            return;
//        }

//        await base.OnTurnAsync(turnContext, cancellationToken);
//    }


//    private void SaveToTranscript(Activity activity)
//    {
//        var convId = activity.Conversation.Id;

//        if (!TranscriptStore.Store.ContainsKey(convId))
//        {
//            TranscriptStore.Store[convId] = new TranscriptRecord
//            {
//                ConversationId = convId
//            };
//        }

//        var clone = activity.CloneActivity();
//        clone.Timestamp = DateTime.UtcNow;

//        TranscriptStore.Store[convId].Activities.Add(clone);
//    }


//}
