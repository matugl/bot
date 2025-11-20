using DirectLineMiddleware.Interfaces;
using DirectLineMiddleware.Transcript;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Configuration;
using Microsoft.Bot.Schema;

public class ProxyBot : ActivityHandler
{
    private readonly IExternalBotClient _external;
    private readonly IOmnichannelService _omnichannel;

    public ProxyBot(IExternalBotClient external, IOmnichannelService omnichannel)
    {
        _external = external;
        _omnichannel = omnichannel;
    }
    //bot externo
    //protected override async Task OnMessageActivityAsync(
    //    ITurnContext<IMessageActivity> turnContext,
    //    CancellationToken cancellationToken)
    //{
    //    var activity = turnContext.Activity;

    //    var userMessage = activity.Text;
    //    var userId = activity.From?.Id ?? "anonymous";
    //    var conversationId = activity.Conversation?.Id ?? "no-conv";

    //    // 1) Mandar mensaje al bot externo
    //    var externalReply = await _external.SendAsync(userMessage, userId, conversationId);

    //    // 2) Responder a Direct Line (que a su vez llega al cliente)
    //    var reply = MessageFactory.Text(externalReply ?? "(sin respuesta)");
    //    await turnContext.SendActivityAsync(reply, cancellationToken);
    //}

    //prueba
    //protected override async Task OnMessageActivityAsync(
    //ITurnContext<IMessageActivity> turnContext,
    //CancellationToken cancellationToken)
    //{
    //    // Simulación de respuesta del bot externo
    //    var externalReply = $"Echo: {turnContext.Activity.Text}";

    //    await turnContext.SendActivityAsync(MessageFactory.Text(externalReply), cancellationToken);
    //}

    //protected override async Task OnMessageActivityAsync(
    //ITurnContext turnContext,
    //CancellationToken cancellationToken)
    //{
    //    // Guardamos lo que Santex envió
    //    SaveToTranscript(turnContext.Activity);

    //    // (tu lógica actual)
    //    var reply = MessageFactory.Text("Echo: " + turnContext.Activity.Text);

    //    // Guardamos la respuesta que envía el middleware
    //    SaveToTranscript(reply);

    //    await turnContext.SendActivityAsync(reply, cancellationToken);
    //}
    protected override async Task OnMessageActivityAsync(
    ITurnContext<IMessageActivity> turnContext,
    CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;

        // Guardamos lo que mandó el usuario (Santex/cliente)
        SaveToTranscript(activity as Activity);

        var userMessage = activity.Text;
        var userId = activity.From?.Id ?? "anonymous";
        var conversationId = activity.Conversation?.Id ?? "no-conv";

        string externalReply;

        try
        {
            // 1) Mandar mensaje al bot externo (python de Santex o lo que sea)
            externalReply = await _external.SendAsync(userMessage, userId, conversationId);
            if (string.IsNullOrWhiteSpace(externalReply))
                externalReply = "(sin respuesta del bot externo)";
        }
        catch (Exception ex)
        {
            // Loguear y responder algo digno
            externalReply = "Tuvimos un problema hablando con el bot externo.";
        }

        // 2) Responder al cliente a través de Direct Line
        var reply = MessageFactory.Text(externalReply);

        // lo marcamos como "bot" para el transcript
        reply.From = new ChannelAccount("mw-bot", "Middleware Bot");
        reply.Conversation = activity.Conversation;

        SaveToTranscript(reply as Activity);

        await turnContext.SendActivityAsync(reply, cancellationToken);
    }


    public override async Task OnTurnAsync(
    ITurnContext turnContext,
    CancellationToken cancellationToken = default)
    {
        // Guardamos toda activity en transcript
        SaveToTranscript(turnContext.Activity as Activity);

        if (turnContext.Activity.Type == ActivityTypes.Event &&
            turnContext.Activity.Name == "escalate")
        {
            var convId = turnContext.Activity.Conversation.Id;

            // 1. Recuperar transcript interno
            var transcript = TranscriptStore.Store[convId].Activities;

            // 2. Crear sesión OC
            var sessionId = await _omnichannel.CreateSessionAsync(turnContext.Activity);

            // 3. Enviar transcript histórico
            await _omnichannel.SendTranscriptAsync(sessionId, transcript);

            // 4. Notificar handoff al agente
            await _omnichannel.TriggerHandoffAsync(sessionId, "Escalado desde bot externo Santex");

            // 5. Confirmar a Santex
            await turnContext.SendActivityAsync("Escalate procesado, sesión creada en Omnichannel.");

            return;
        }

        await base.OnTurnAsync(turnContext, cancellationToken);
    }


    private void SaveToTranscript(Activity activity)
    {
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
