# PC Monitor USB 2.0.2

Painel local que transforma qualquer celular Android compatível em um display USB de sensores e controle para Windows. O uso normal não depende de Wi-Fi, internet, conta, nuvem, telemetria ou servidor externo.

## Arquivos prontos

Na pasta `Release-v2.0.2`:

- `PCMonitorServer.exe`: servidor Windows 10/11 x64, autocontido e em arquivo único;
- `PCMonitorUSB.apk`: aplicativo Android assinado, compatível com Android 5.0/API 21 ou superior;
- `README.md`: manual de instalação e uso;
- avisos das licenças utilizadas.

Mantenha o EXE e o APK na mesma pasta. O servidor instala ou atualiza o APK automaticamente quando encontra um celular autorizado.

## Primeira configuração

1. No celular, ative **Opções do desenvolvedor** tocando sete vezes em **Número da versão**.
2. Ative **Depuração USB**.
3. Execute `PCMonitorServer.exe` e aceite a confirmação de administrador. Esse acesso é necessário para aumentar a leitura de temperatura, clock e potência do hardware.
4. Clique em **Configurar celular**. No primeiro uso, confirme o download oficial do Android Platform-Tools.
5. Conecte um cabo USB com suporte a dados.
6. Desbloqueie o celular, aceite a chave RSA e marque **Sempre permitir deste computador**.
7. Aguarde a instalação e a abertura automáticas do painel.

Depois disso, o uso diário é apenas ligar o PC e conectar o cabo USB. O mesmo cabo transporta dados e fornece alimentação.

### Mensagens comuns

- **Celular desconectado**: confira cabo, porta USB e depuração USB.
- **Aceite a autorização de depuração USB**: desbloqueie o aparelho e confirme a chave RSA.
- **Dispositivo ADB offline**: reconecte o cabo.
- **Android antigo demais**: é necessário Android 5.0/API 21 ou superior.
- **ADB por rede ignorado**: somente aparelho físico ligado por USB é aceito.

## Painel Android

- **Monitor** mostra somente sensores: temperatura, uso, GHz/MHz e watts da CPU e GPU, hotspot, VRAM, RAM, ventoinha, rede e disco quando disponíveis.
- **Controle** usa uma tela separada, mantém um resumo de CPU/GPU/RAM/VRAM e dedica o restante aos botões grandes e alinhados.
- Portrait e landscape possuem layouts próprios. No horizontal, CPU e GPU usam áreas simétricas e centralizadas.
- O menu `⋮` oferece brilho normal, baixo ou mínimo e proteção discreta da tela.
- A tela fica ligada enquanto a comunicação está ativa. Se a conexão cair, os valores viram `--` e deixam de parecer atuais.

## Aplicativo Windows

A visão geral possui **Ligar/Desligar servidor** e **Configurar celular**. O primeiro botão alterna o servidor local sem fechar o aplicativo; o texto e a cor acompanham o estado atual. Ao minimizar ou fechar a janela, o aplicativo some da barra de tarefas e permanece somente na área de notificação.

Na mesma tela é exibida a configuração realmente detectada no PC:

- nome do computador e versão do Windows;
- fabricante e modelo da placa-mãe;
- modelo exato da CPU;
- GPU principal e GPUs adicionais;
- quantidade total de RAM.

A GPU principal não é escolhida pela posição em uma lista. O coletor agrupa cada placa pelo identificador físico do LibreHardwareMonitor, pontua os sensores reais disponíveis e usa um desempate estável. Assim, temperatura, clock, potência e VRAM nunca são misturados entre placas diferentes.

### Sensores

O servidor usa `LibreHardwareMonitorLib` 0.9.6 e seleciona cada leitura pela combinação de `HardwareType`, `SensorType`, nome e identificador. Valores ausentes são mostrados como `--`, nunca estimados.

A aba **Sensores** permite atualizar a lista, exportar `sensors.txt` e, somente quando necessário, instalar suporte adicional de baixo nível. Essa instalação sempre exige confirmação e pode pedir uma reinicialização posterior; o programa nunca reinicia o computador sozinho.

### Segurança dos controles

O celular envia somente IDs permitidos, como `volume_up` ou `media_play_pause`. O servidor não aceita caminhos, PowerShell, CMD ou comandos arbitrários recebidos do APK. Botões personalizados são configurados localmente no Windows e limitados a programas/atalhos existentes, URLs válidas, teclas permitidas e ações internas.

O servidor escuta exclusivamente em `127.0.0.1`. Não há CORS aberto, binding público, UPnP, telemetria ou encaminhamento no roteador.

## API local

- `GET /api/stats`: sensores atuais;
- `GET /api/system`: configuração identificada do PC;
- `GET /api/config`: layout, limites térmicos e botões;
- `GET /api/ping`: teste autenticado;
- `POST /api/command`: ação permitida, autenticada pelo token temporário `X-PCMonitor-Token`.

O ADB reverse transporta as requisições do loopback do Android para o loopback do Windows, somente pelo cabo USB.

## Compatibilidade

- Windows 10/11 x64;
- CPU AMD ou Intel;
- GPU AMD, NVIDIA ou Intel, incluindo computadores com vídeo integrado e dedicado;
- Android 5.0/API 21 ou superior;
- FPS permanece oculto e `null` enquanto não existir uma fonte real.

A disponibilidade de hotspot, potência e ventoinha depende do que o hardware e o driver expõem. O aplicativo não altera BIOS, PBO, overclock, undervolt, drivers, plano de energia ou ajustes da GPU.

## Dados locais

- configuração: `%LOCALAPPDATA%\PCMonitorUSB\config.json`;
- logs rotativos: `%LOCALAPPDATA%\PCMonitorUSB\logs\app.log`;
- Platform-Tools: `%LOCALAPPDATA%\PCMonitorUSB\platform-tools`.

Na primeira execução 2.0, dados e inicialização automática da versão anterior são migrados. No celular, o aplicativo anterior só é removido depois que a nova instalação for validada.

## Compilar

Windows:

```powershell
dotnet publish Windows\PCMonitorServer\PCMonitorServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o Release-v2.0.2
```

Android: use JDK 17, Gradle 8.2.1 e Android SDK 34 para executar `assembleRelease`; alinhe e assine o APK antes de instalar.

## Estrutura

```text
PCMonitorUSB/
├── Windows/PCMonitorServer/
├── Windows/PCMonitorServer.Tests/
├── Android/app/src/main/java/com/pcmonitorusb/
├── Android/app/src/main/res/layout/
├── Android/app/src/main/res/layout-land/
├── docs/TEST-REPORT.md
└── Release-v2.0.2/
```

Referências: [ADB reverse](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server), [Android Debug Bridge](https://developer.android.com/tools/adb), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
