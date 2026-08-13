# PC Monitor USB 2.2.0

[English](README.md)

O PC Monitor USB transforma um celular Android compatível em um painel leve de monitoramento e controle do Windows por USB. O funcionamento normal não depende de Wi-Fi, internet, nuvem, conta, telemetria ou assinatura.

## Principais recursos

- Temperatura, uso, clock atual e potência da CPU, quando disponibilizados pelo hardware.
- Temperatura, hotspot, uso, clocks do núcleo e da VRAM, memória, potência e ventoinha da GPU, quando disponíveis.
- RAM, tráfego de rede opcional e atividade do disco.
- Modos Monitor e Controle separados e adaptados para as orientações vertical e horizontal.
- Controles de mídia, volume, área de trabalho, Gerenciador de Tarefas, Steam, AMD Software e ações configuradas localmente.
- Detecção USB, `adb reverse`, instalação/atualização do APK e abertura automática após a autorização inicial.
- Servidor local restrito a `127.0.0.1`.
- Interface em português do Brasil e inglês no Windows e no Android.
- Tela local e opcional de Wake-on-LAN para ligar o PC pelo celular quando o painel USB estiver desconectado.

## Idioma

Na primeira execução, o aplicativo Windows acompanha o idioma de exibição do Windows: sistemas em português usam português; os demais usam inglês. É possível alterar essa escolha em **Configurações > Idioma**, selecionando **Automático (Windows)**, **Português** ou **English**. Feche e abra novamente o aplicativo Windows depois de mudar essa opção.

O idioma escolhido no Windows é enviado pela API local USB, e o painel Android passa a usar o mesmo idioma automaticamente. Nenhum serviço de tradução on-line é utilizado.

## Configuração inicial

1. No celular, ative **Opções do desenvolvedor** tocando sete vezes em **Número da versão**.
2. Ative **Depuração USB**.
3. Execute `PCMonitorServer.exe` e aceite a solicitação de administrador. O acesso elevado amplia a leitura dos sensores.
4. Clique em **Configurar celular**. No primeiro uso, autorize o download oficial do Android Platform-Tools.
5. Conecte o celular usando um cabo USB com suporte a dados.
6. Desbloqueie o celular, aceite a chave RSA e marque **Sempre permitir deste computador**.
7. Aguarde a instalação e a abertura automáticas do APK.

Depois disso, o uso diário consiste apenas em ligar o computador e conectar o cabo USB. O mesmo cabo transporta os dados e alimenta o celular.

## Aplicativo Windows

A página **Visão geral** mostra a configuração realmente identificada no PC: versão do Windows, placa-mãe, CPU, GPU principal e GPUs adicionais, além da RAM instalada. Ela também oferece **Ligar/Desligar servidor** e **Configurar celular**. Ao minimizar ou fechar a janela, o aplicativo vai para a área de notificação e deixa de aparecer na barra de tarefas.

A página **Sensores** lista hardware, tipo, valor e identificadores estáveis. Ela também exporta `sensors.txt` para diagnóstico. O LibreHardwareMonitor é a fonte principal; valores ausentes aparecem como `--` e nunca são inventados.

Em computadores nos quais a temperatura, o clock ou a potência da CPU exigem acesso de baixo nível, **Ampliar suporte aos sensores** pode baixar o instalador oficial do PawnIO após confirmação explícita e verificação do SHA-256. O PC Monitor USB nunca reinicia o computador automaticamente.

## Painel Android

- **Monitor** prioriza informações detalhadas de CPU, GPU, RAM e VRAM.
- **Controle** mantém um resumo ao vivo e usa o espaço restante para botões grandes e alinhados.
- As orientações vertical e horizontal possuem layouts próprios.
- O menu `⋮` controla somente o brilho da tela do aplicativo e o modo opcional de proteção.
- Se a comunicação for perdida, os valores antigos são substituídos por `--` e ficam visualmente escurecidos.
- Com o Wake-on-LAN habilitado, o painel desconectado muda para uma tela exclusiva **Ligar computador**. O celular guarda apenas o nome do PC, MAC da Ethernet, broadcast da sub-rede e porta UDP fixa 9 recebidos pela API USB autenticada.

## Ligar o PC pelo celular

O Wake-on-LAN é opcional e usado somente enquanto o PC está desligado; o monitoramento e os controles normais continuam funcionando somente pelo USB.

1. Conecte o PC ao roteador por cabo Ethernet. O celular pode usar o Wi-Fi desse mesmo roteador.
2. Habilite Wake-on-LAN ou **Resume by PCI-E/Networking Device** no firmware do PC e **Wake on Magic Packet** no adaptador Ethernet do Windows.
3. Em **Configurações**, mantenha **Mostrar 'Ligar computador' no celular quando desconectado** habilitado.
4. Ative **Iniciar com Windows** para o servidor, ADB reverse e painel Android reconectarem automaticamente após o PC iniciar.
5. Conecte o celular uma vez com o servidor em execução. A configuração autenticada ficará guardada privadamente no APK.

Quando o USB desaparecer, abra ou mantenha o aplicativo Android na tela e toque em **Ligar computador**. O APK envia três magic packets padrão para a rede local. Ele não acessa internet, nuvem nem relay público. O funcionamento após desligamento completo depende do firmware da placa-mãe, adaptador Ethernet, driver e estado de energia do Windows; computadores ligados apenas por Wi-Fi normalmente não suportam esse método.

## Segurança

O celular envia somente IDs de comandos permitidos. Ele não pode enviar caminhos arbitrários, PowerShell, CMD ou comandos de shell. Os destinos de ações personalizadas ficam armazenados e são validados no Windows.

O servidor HTTP escuta somente em `127.0.0.1`; o transporte USB usa ADB reverse autenticado. Todos os endpoints `/api/*` exigem um token temporário de 192 bits, recriado sempre que o servidor Windows é iniciado. As requisições possuem limite rígido de tamanho, os comandos têm limitação de frequência e tokens duplicados ou inválidos são rejeitados. Não há exposição pública, encaminhamento de portas no roteador, UPnP, analytics ou telemetria.

O Wake-on-LAN não adiciona uma porta de entrada. O APK valida o MAC fornecido pelo servidor, o destino IPv4 e a porta UDP fixa 9 antes de enviar um broadcast local padrão. O celular não pode escolher um destino pela API de comandos.

Quando a inicialização automática é ativada, a tarefa elevada aponta para uma cópia protegida em Arquivos de Programas, e não para um EXE portátil gravável pelo usuário. Consulte [SECURITY.md](SECURITY.md) e o [relatório de testes de segurança](docs/SECURITY-REPORT.md).

## Compatibilidade

- Windows 10/11 x64.
- Hardware AMD, Intel ou NVIDIA compatível com o LibreHardwareMonitor.
- Android 5.0/API 21 ou superior; o Android 8.1 Go é um dos alvos principais.
- O uso normal não exige Android Studio nem instalação separada do ADB.

A disponibilidade exata dos sensores depende da placa-mãe, GPU, firmware e driver. O aplicativo não modifica BIOS, PBO, overclock, undervolt, drivers, plano de energia ou ajustes da GPU.

## Dados locais

- Configuração: `%LOCALAPPDATA%\PCMonitorUSB\config.json`
- Log rotativo: `%LOCALAPPDATA%\PCMonitorUSB\logs\app.log`
- Platform-Tools: `%LOCALAPPDATA%\PCMonitorUSB\platform-tools`

## Compilação

O Windows exige o SDK do .NET 8:

```powershell
dotnet publish Windows\PCMonitorServer\PCMonitorServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o Release-v2.2.0
```

O Android exige JDK 17, Gradle 8.2.1 e Android SDK 34. Execute `assembleRelease` e depois alinhe e assine o APK.

Referências: [ADB reverse](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server), [Android Debug Bridge](https://developer.android.com/tools/adb), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), [comportamento do Wake-on-LAN no Windows](https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/wake-on-lan-feature) e [manual da MSI B550M PRO-VDH WIFI](https://download.msi.com/archive/mnu_exe/mb/B550MPRO-VDHWIFICEC.pdf).
