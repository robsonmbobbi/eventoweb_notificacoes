# eventoweb_notificacoes

Worker que envia notificações (Email/WhatsApp) atuando como **servidor RPC do RabbitMQ**
(padrão request/reply do RabbitMQ, com `ReplyTo`/`CorrelationId`).

Quem chama via RPC é responsável por resolver o template (assunto/corpo) e as variáveis
antes de publicar o pedido, e por persistir o status de envio no seu próprio banco. O
serviço não tem persistência local nem interface web: toda a configuração vem do
`appsettings.json` (ou variáveis de ambiente) e é lida no boot.

O envio de WhatsApp usa a **API oficial da Meta (WhatsApp Cloud API)**, chamada por HTTPS
na Graph API. (Versões anteriores usavam BaileysCSharp, emulando o WhatsApp Web via QR Code.)

## Configuração

Tudo fica no **`appsettings.json`** (não versionado) ou em variáveis de ambiente equivalentes
(ex.: `RabbitMq__HostName`, `Whatsapp__AccessToken`). A configuração é lida só no boot —
**qualquer mudança exige reiniciar o processo**.

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
    "Email": { "ApiUrl": "...", "ApiKey": "...", "FromAddress": "..." }
  },
  "Whatsapp": {
    "PhoneNumberId": "...",
    "WabaId": "...",
    "AccessToken": "...",
    "ApiVersion": "v23.0",
    "WebhookVerifyToken": "..."
  },
  "Canais": {
    "Email": true,
    "Whatsapp": true
  }
}
```

O processo **falha ao subir**, com mensagem clara, se nenhum canal estiver habilitado ou se
um canal habilitado estiver com a configuração incompleta (`Notification:Email` sem
`ApiUrl`/`ApiKey`/`FromAddress`, ou `Whatsapp` sem `PhoneNumberId`/`WabaId`/`AccessToken`/
`ApiVersion`). A seção de um canal desabilitado pode ficar vazia.

Com WhatsApp habilitado, o consumo da fila só começa depois que as credenciais forem
validadas na Graph API (feito automaticamente no boot, com retry). A conexão com o RabbitMQ
tem seu próprio laço de reconexão.

### Canais (`Canais:Email` / `Canais:Whatsapp`)

Controla quais canais este processo serve (default `true` para ambos se a seção não existir).

Um pedido de notificação para um canal desabilitado nesta instância não derruba o
processo: a resposta RPC volta com `Sucesso: false` e
`MensagemErro: "Meio de notificação não configurado neste worker: ..."`.

**Deploy dividido por máquina** (ex.: Email numa máquina com IP fixo para o allowlist do
provedor): suba cada
instância com o `Canais` correto no seu `appsettings.json` e configure `RabbitMq:RequestQueueName` com uma fila própria por máquina, por
exemplo:

- Máquina WhatsApp: canal Email desabilitado, `RabbitMq:RequestQueueName=notificacoes.enviar.whatsapp`.
- Máquina Email: canal WhatsApp desabilitado, `RabbitMq:RequestQueueName=notificacoes.enviar.email`.

Como a fila é uma única string plana (`RabbitMq:RequestQueueName`) e ambas as instâncias
rodam o mesmo código, cada uma deve apontar para uma fila própria — do contrário, com as
duas consumindo a mesma fila, o RabbitMQ pode entregar uma mensagem de WhatsApp para a
instância de Email (ou vice-versa) por round-robin entre consumidores concorrentes.
**Rotear cada mensagem para a fila do canal certo (pelo campo `meio`) é responsabilidade
de quem publica** (fora deste repositório) — este serviço não declara topologia nem faz
esse roteamento entre filas; ele só se defende de receber a mensagem errada respondendo
com erro via RPC, como descrito acima.

## WhatsApp (Cloud API da Meta)

### Cadastro na Meta (uma vez)

1. **Portfólio empresarial** em business.facebook.com.
2. **App** em developers.facebook.com → *Create App* → tipo **Business** → adicionar o
   produto **WhatsApp**. Já vem uma WABA de teste com um **número de teste** que envia para
   até 5 destinatários cadastrados na tela *API Setup* — use para desenvolvimento.
3. **Número de produção** (WhatsApp Manager → Números de telefone → Adicionar): o número não
   pode estar ativo no app WhatsApp/WhatsApp Business e precisa receber SMS/ligação para
   verificação. O **nome de exibição** passa por revisão da Meta.
4. **Forma de pagamento** no WhatsApp Manager (cobrança por template entregue — ver
   developers.facebook.com/docs/whatsapp/pricing).
5. **Verificação da empresa** é opcional para até 250 destinatários únicos/24h (limite de
   portfólio não verificado). Só é necessária para volumes maiores.
6. **Token permanente**: Configurações do negócio → Usuários → **Usuários do sistema** →
   criar usuário admin → atribuir o App e a WABA (controle total) → *Gerar token* com
   `whatsapp_business_messaging` e `whatsapp_business_management`, validade "Nunca". O
   token temporário da tela *API Setup* expira em 24h.
7. Copiar **Phone Number ID** e **WABA ID** (App → WhatsApp → API Setup) e preencher, junto
   com o token, na seção `Whatsapp` do `appsettings.json`.
8. **Templates** (WhatsApp Manager → Modelos de mensagem): categoria **Utilidade**, idioma
   `pt_BR`, **variáveis nomeadas** (`{{nome_participante}}`) com os mesmos nomes das chaves
   do `dadosJson`. Para anexo, cabeçalho do tipo **Documento** (ou Imagem/Vídeo). Evite
   linguagem promocional, senão a Meta reclassifica como Marketing.
9. Só envie para quem consentiu (ex.: checkbox na inscrição) — bloqueios e denúncias
   derrubam a *quality rating* do número, registrada no log ao validar no boot.

### Como o envio funciona

- Mensagens iniciadas pela empresa só podem ser **templates aprovados**. O pedido RPC
  informa `templateWhatsapp` (+ `idiomaTemplateWhatsapp`, padrão `pt_BR`);
  `templateConteudo` não é usado nesse canal.
- O worker consulta o template na Graph API (cache de 10 min) e preenche as variáveis do
  cabeçalho/corpo com as chaves **de primeiro nível** de `dadosJson`. Templates com
  parâmetros posicionais (`{{1}}`, `{{2}}`) usam as chaves `"1"`, `"2"`. Variável faltando →
  erro antes de chamar a Meta. Quebras de linha/tabs nos valores viram espaço (a Meta as
  rejeita em parâmetros).
- **Anexo**: no máximo 1, e só com template de cabeçalho de mídia (o arquivo é enviado via
  upload de mídia e usado no cabeçalho). Template com cabeçalho de mídia sem anexo, anexo
  com template sem cabeçalho de mídia ou 2+ anexos → erro.
- `Sucesso: true` significa que a Meta **aceitou** a mensagem. Falhas de entrega posteriores
  (ex.: número sem WhatsApp) não voltam pela RPC — só aparecem no log, via webhook de status
  (ver abaixo). O envio também loga o `wa_id` devolvido pela Meta e avisa quando ele difere
  do número enviado (nono dígito BR).
- Credenciais são validadas automaticamente no boot (com retry), então um restart volta a
  consumir a fila sem intervenção. Não há limite diário próprio: o teto de envios é o da Meta
  (250 destinatários únicos/24h para portfólio não verificado).
- Escopo é de **instância única** — um número de WhatsApp para o processo todo.

Erros comuns da Meta aparecem traduzidos em `mensagemErro`: `190` token inválido/expirado,
`132001` template inexistente no idioma, `132000` quantidade de parâmetros errada,
`131047` fora da janela de 24h, `131056`/`130429` limite de taxa.

### Webhook de status

A Cloud API não permite consultar o status de uma mensagem pelo `wamid`; enviada, entregue,
lida ou falha só chegam por webhook. O worker expõe `GET/POST /webhook/whatsapp` e registra
cada status no log (falhas em Warning, com `code`, `title` e `details`).

1. Defina um **verify token** qualquer em `Whatsapp:WebhookVerifyToken` no `appsettings.json`.
2. Sem endereço público (desenvolvimento), abra um túnel:
   `cloudflared tunnel --url http://localhost:<porta>` → gera `https://xxxx.trycloudflare.com`
   (a URL muda a cada execução).
3. App da Meta → WhatsApp → **Configuration** → Webhook: *Callback URL*
   `https://<host>/webhook/whatsapp` e o mesmo verify token → *Verify and save*.
4. Em *Webhook fields*, assine **messages**.

Códigos de falha comuns: `131049` template de marketing barrado pela Meta (use Utilidade),
`131026` destinatário sem WhatsApp/versão antiga/bloqueou, `131042` problema de pagamento
da WABA, `131030` destinatário fora da lista do número de teste.

A assinatura `X-Hub-Signature-256` ainda não é validada (exigiria o App Secret); o endpoint
só registra logs, então o risco é apenas de logs falsos.

## Contrato RPC

Requisição (`NotificacaoRequest`), publicada na fila `RabbitMq:RequestQueueName` com
`BasicProperties.CorrelationId` e `BasicProperties.ReplyTo` preenchidos:

```json
{
  "id": "guid",
  "meio": "EMail | WhatsApp",
  "destinatario": "email ou telefone",
  "templateAssunto": "usado apenas para EMail",
  "templateConteudo": "corpo do template (Scriban) — usado apenas para EMail",
  "dadosJson": "{ \"variavel\": \"valor\" }",
  "templateWhatsapp": "nome do template aprovado na Meta — obrigatório para WhatsApp",
  "idiomaTemplateWhatsapp": "pt_BR",
  "anexos": [
    { "nomeArquivo": "comprovante.pdf", "mimeType": "application/pdf", "conteudo": "<base64>" }
  ]
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

### Entrega confiável e assíncrona da resposta

**Não use mais o `amq.rabbitmq.reply-to`** (direct reply-to). Ele é uma
pseudo-fila atrelada à conexão AMQP do cliente no momento exato da resposta —
se o cliente cair entre publicar o pedido e o servidor responder, a resposta
é perdida para sempre, sem fila real e sem redelivery.

Em vez disso, o cliente deve:

1. **Declarar sua própria fila de resposta**, durável e com **nome fixo por
   instância** (não uma fila nova a cada mensagem) — por exemplo
   `notificacoes.status.<nome-da-instancia-cliente>` — com
   `durable: true, exclusive: false, autoDelete: false`. A declaração deve
   acontecer na subida da instância cliente, antes de publicar qualquer
   pedido; é idempotente (`queue.declare` com os mesmos argumentos é um
   no-op).
2. Publicar o pedido com `ReplyTo` apontando para essa fila e `CorrelationId`
   preenchido — como essa fila é compartilhada entre todos os pedidos em voo
   da mesma instância, o `CorrelationId` é **obrigatório na prática** para
   casar cada resposta com o pedido certo.
3. Consumir essa fila de forma duradoura (ack manual só depois de persistir o
   resultado no próprio banco do cliente) — sobrevive a quedas e reinícios do
   cliente, porque a fila e as mensagens são persistentes.

Do lado do servidor: a resposta é publicada com `DeliveryMode` persistente e
usando *publisher confirms* (`mandatory: true` + confirmação do broker). O
servidor só dá `ack` no pedido original **depois** de confirmar que a
resposta foi aceita/roteada pelo broker. Se a publicação falhar — broker
fora do ar, ou a fila informada em `ReplyTo` não existir —, o pedido **não**
é ack'd e volta para a fila de request, sendo reprocessado quando a conexão
for restabelecida. Ou seja: uma resposta que não chega significa "o pedido
ainda está pendente/será reprocessado", nunca "perdida silenciosamente".

**Trade-off aceito**: o servidor não mantém nenhum estado próprio de
notificações já processadas (sem persistência/idempotência aqui). Existe uma
janela pequena entre "notificação enviada com sucesso" e "confirmação da
publicação da resposta" em que uma queda do servidor pode causar
reprocessamento do mesmo pedido — e, consequentemente, reenvio duplicado.
Isso é deliberado, para não introduzir persistência de dados de negócio no
servidor (que continua sendo responsabilidade do cliente).

Se uma notificação de WhatsApp falhar (Meta indisponível, limite de taxa etc.), o
envio falha rápido (`Sucesso: false`) em vez de enfileirar/esperar — o chamador já é
responsável por retry, então não há uma segunda fila de retry escondida aqui.

## Rodando localmente

Requer RabbitMQ acessível (local ou remoto).

```bash
dotnet run
```

Preencha antes o `appsettings.json` (ver [Configuração](#configuração)). O log
`Consumindo fila '<fila>' em <host>:<porta>` indica que o worker está pronto. A porta HTTP
(`Now listening on: ...`) só é usada pelo webhook do WhatsApp.

A fila (`RabbitMq:RequestQueueName`) é criada/gerenciada pela infra — este serviço não
declara topologia, apenas consome; se a fila ainda não existir, o consumo falha e o
serviço entra no laço de reconexão até que ela exista. A UI de management do RabbitMQ (se
o plugin estiver habilitado) permite acompanhar filas e mensagens em trânsito.
