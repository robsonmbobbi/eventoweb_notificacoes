# eventoweb_notificacoes

Worker que envia notificações (Email/WhatsApp) atuando como **servidor RPC do RabbitMQ**
(padrão request/reply do RabbitMQ, com `ReplyTo`/`CorrelationId`), com uma **interface web
de configuração** embutida no próprio processo.

Quem chama via RPC é responsável por resolver o template (assunto/corpo) e as variáveis
antes de publicar o pedido, e por persistir o status de envio no seu próprio banco — isso
não mudou. O que mudou: o serviço agora tem persistência local própria (SQLite-like,
embutida via LiteDB) para guardar a configuração de RabbitMQ/Email e o estado da sessão do
WhatsApp, editável em runtime pela UI web, em vez de depender só de `appsettings.json`
estático.

O envio de WhatsApp não usa mais uma API REST externa (Evolution API) — usa
[BaileysCSharp](https://github.com/WhiskeySockets/BaileysCSharp), conectando diretamente
no protocolo multi-device do WhatsApp Web via WebSocket, sem gateway intermediário.

## Configuração

O serviço só começa a consumir a fila do RabbitMQ quando: (1) a configuração do RabbitMQ
está validada, **e** (2) todos os canais habilitados nesta instância (`Canais:Email` /
`Canais:Whatsapp`, ver abaixo) também estão validados. Enquanto isso não acontece, o
processo fica de pé servindo só a interface de configuração.

**Primeira execução**: abra `http://localhost:<porta>/` (redireciona para `/admin.html`) e
preencha RabbitMQ, Email e/ou escaneie o QR Code do WhatsApp por lá. Não é mais necessário
editar `appsettings.json` à mão para isso.

**`appsettings.json`** (não versionado) ainda existe, mas agora serve só de **seed**: na
primeira execução, se a configuração salva (LiteDB) estiver vazia e a seção
correspondente do `appsettings.json` estiver preenchida, ela é usada para popular o banco
uma única vez. Depois disso, edições passam a ser feitas pela UI — reeditar
`appsettings.json` não tem mais efeito.

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
    "SessionName": "eventoweb"
  },
  "Storage": {
    "ConfigDbPath": "./data/notificacoes-config.db"
  },
  "Canais": {
    "Email": true,
    "Whatsapp": true
  }
}
```

`Whatsapp:SessionName` e `Storage:ConfigDbPath` são exceção: continuam vindo só do
`appsettings.json`/variáveis de ambiente, nunca da UI/LiteDB (são decisões de
infraestrutura/deploy, não algo que se preenche num formulário). `Canais` **não** é
exceção — segue o mesmo padrão de seed do RabbitMq/Email descrito acima (ver a seguir).

### Canais (`Canais:Email` / `Canais:Whatsapp`)

Controla quais canais este processo serve. Editável pela seção "Canais habilitados" em
`/admin.html` (`GET`/`PUT /api/config/canais`) — **a mudança tem efeito imediato, sem
precisar reiniciar o processo**: desmarcar WhatsApp derruba a conexão/socket do
BaileysCSharp na hora; marcar volta a tentar conectar na hora. A sessão do WhatsApp
(`creds.json`) é preservada ao desabilitar — religar reconecta sem pedir um novo QR Code;
só o logout manual ("Sair / trocar número") ou um logout remoto pelo próprio WhatsApp
apagam as credenciais. Enquanto o canal está desabilitado, o status mostrado é
`Desabilitado` (distinto de `Desconectado`, que significa "tentando reconectar").

`appsettings.json`'s `Canais` (default `{ "Email": true, "Whatsapp": true }` se a seção
não existir) serve só de **seed no primeiro boot** — depois disso, edições passam a ser
feitas pela UI, igual RabbitMq/Email. Se os dois estiverem `false` nesse seed inicial, o
processo falha ao subir com uma mensagem de erro clara; depois do primeiro boot, tentar
desabilitar os dois pela UI é rejeitado com `400` (pelo menos um precisa continuar
habilitado).

Um pedido de notificação para um canal desabilitado nesta instância não derruba o
processo: a resposta RPC volta com `Sucesso: false` e
`MensagemErro: "Meio de notificação não configurado neste worker: ..."`.

**Deploy dividido por máquina** (ex.: WhatsApp numa máquina com mais memória para o socket
do BaileysCSharp, Email numa máquina com IP fixo para o allowlist do provedor): suba cada
instância com o `Canais` inicial já correto (seed) ou ajuste pela UI logo após o primeiro
boot, e configure `RabbitMq:RequestQueueName` com uma fila própria por máquina, por
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

### Volumes persistentes (deploy)

Dois caminhos em disco precisam sobreviver a um redeploy/restart do container, ou a
configuração/sessão se perde:

- `Storage:ConfigDbPath` (padrão `./data/notificacoes-config.db`) — configuração de
  RabbitMQ/Email salva pela UI.
- A pasta de sessão do WhatsApp, calculada pelo próprio BaileysCSharp como
  `<diretório do executável>/<Whatsapp:SessionName>/` (contém `creds.json` e as chaves de
  sessão). O caminho exato é logado no início (`Sessão do WhatsApp armazenada em ...`).

Perder o primeiro derruba toda a configuração salva; perder o segundo exige escanear o QR
de novo.

## WhatsApp (BaileysCSharp)

- A biblioteca é consumida como **git submodule** em `external/BaileysCSharp`, fixada no
  commit `0a3600e026d6fde2fb760d52f6b11e2169a32dd1` (não há pacote NuGet nem releases) —
  atualizar é uma ação deliberada (`cd external/BaileysCSharp && git checkout
  <novo-commit> && cd ../.. && git add external/BaileysCSharp`).
- BaileysCSharp fixa uma versão do protocolo WhatsApp Web que fica desatualizada em poucos
  meses (o WhatsApp passa a rejeitar o handshake com `failure reason='405'`). O worker
  busca a versão vigente automaticamente na inicialização, do arquivo mantido pelo projeto
  Baileys (JS) em `raw.githubusercontent.com/WhiskeySockets/Baileys/master/src/Defaults/baileys-version.json`;
  se a busca falhar (sem internet, GitHub fora do ar), cai para a versão padrão da lib.
- Só existe pareamento por **QR Code** (a lib não implementa pairing-code por telefone). O
  QR aparece tanto no console (ASCII) quanto na página `/admin.html` (imagem), enquanto o
  status estiver `AguardandoQrCode`.
- Se a sessão for invalidada pelo próprio WhatsApp (logout remoto), o serviço descarta as
  credenciais salvas automaticamente e gera um QR novo, sem precisar reiniciar o processo.
  O botão "Sair / trocar número" na UI faz o mesmo manualmente.
- Escopo é de **instância única** — uma sessão de WhatsApp para o processo todo, não por
  evento/organizador (diferente do backend legado `eventoweb_comum_back`, que é
  multi-tenant via a tabela `configuracoes_whatsapp`).

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

Se uma notificação de WhatsApp for pedida enquanto a sessão não está `Conectado`, o envio
falha rápido (`Sucesso: false`) em vez de enfileirar/esperar — o chamador já é responsável
por retry, então não há uma segunda fila de retry escondida aqui.

## Rodando localmente

Requer RabbitMQ acessível (local ou remoto). Como este é o primeiro clone com o submodule:

```bash
git submodule update --init --recursive
dotnet run
```

Os logs indicam a porta em uso (`Now listening on: https://localhost:5001`, por exemplo).
Abra essa URL no navegador — ela redireciona para `/admin.html` — e configure RabbitMQ,
Email e/ou WhatsApp por ali. O worker só passa a consumir `notificacoes.enviar` depois que
o banner do topo da página mostrar "Pronto".

A fila (`RabbitMq:RequestQueueName`) é criada/gerenciada pela infra — este serviço não
declara topologia, apenas consome; se a fila ainda não existir, o consumo falha e o
serviço entra no laço de reconexão até que ela exista. A UI de management do RabbitMQ (se
o plugin estiver habilitado) permite acompanhar filas e mensagens em trânsito.
