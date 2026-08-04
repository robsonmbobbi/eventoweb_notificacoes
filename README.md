# eventoweb_notificacoes

Worker que envia notificações (Email/WhatsApp) atuando como **servidor RPC do RabbitMQ**
(padrão request/reply do RabbitMQ, com `ReplyTo`/`CorrelationId`).

O serviço não acessa banco de dados: ele apenas consome pedidos de envio de uma fila,
envia a notificação e responde sucesso/erro pela fila de reply. Quem chama via RPC é
responsável por resolver o template (assunto/corpo) e as variáveis antes de publicar o
pedido, e por persistir o status de envio no seu próprio banco.

## Configuração (`appsettings.json`, não versionado)

```json
{
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "RequestQueueName": "notificacoes.enviar",
    "PrefetchCount": 1
  },
  "Notification": {
    "Email": { "ApiUrl": "...", "ApiKey": "...", "FromAddress": "..." },
    "Whatsapp": { "ApiUrl": "...", "ApiKey": "...", "Instance": "..." }
  }
}
```

Pelo menos um dos meios (`Notification:Email` ou `Notification:Whatsapp`) precisa estar
configurado; o serviço falha ao iniciar caso nenhum esteja.

## Contrato RPC

Requisição (`NotificacaoRequest`), publicada na fila `RabbitMq:RequestQueueName` com
`BasicProperties.CorrelationId` e `BasicProperties.ReplyTo` preenchidos:

```json
{
  "id": "guid",
  "meio": "EMail | WhatsApp",
  "destinatario": "email ou telefone",
  "templateAssunto": "usado apenas para EMail",
  "templateConteudo": "corpo do template (Scriban)",
  "dadosJson": "{ \"variavel\": \"valor\" }"
}
```

Resposta (`NotificacaoResponse`), publicada em `ReplyTo` com o mesmo `CorrelationId`:

```json
{
  "id": "guid",
  "sucesso": true,
  "mensagemErro": null
}
```

Recomenda-se que o cliente RPC use *direct reply-to* do RabbitMQ
(`ReplyTo = "amq.rabbitmq.reply-to"`) — não é necessário declarar fila própria.

## Rodando localmente

Requer um RabbitMQ acessível (local ou remoto) com as credenciais configuradas em
`appsettings.json`. A fila (`RabbitMq:RequestQueueName`) é criada/gerenciada pela
infra — este serviço não declara topologia, apenas consome; se a fila ainda não
existir, o consumo falha e o serviço entra no laço de reconexão até que ela exista.
Com o broker no ar:

```bash
dotnet run
```

Os logs indicam o consumidor ativo na fila configurada. A UI de management do RabbitMQ
(se o plugin estiver habilitado) permite acompanhar filas e mensagens em trânsito.
