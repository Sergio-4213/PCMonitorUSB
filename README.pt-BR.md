# PC Monitor USB 2.1.1

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

## Segurança

O celular envia somente IDs de comandos permitidos. Ele não pode enviar caminhos arbitrários, PowerShell, CMD ou comandos de shell. Os destinos de ações personalizadas ficam armazenados e são validados no Windows.

O servidor HTTP escuta somente em `127.0.0.1`; o transporte USB usa ADB reverse autenticado. Todos os endpoints `/api/*` exigem um token temporário de 192 bits, recriado sempre que o servidor Windows é iniciado. As requisições possuem limite rígido de tamanho, os comandos têm limitação de frequência e tokens duplicados ou inválidos são rejeitados. Não há exposição pública, encaminhamento de portas no roteador, UPnP, analytics ou telemetria.

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
dotnet publish Windows\PCMonitorServer\PCMonitorServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o Release-v2.1.1
```

O Android exige JDK 17, Gradle 8.2.1 e Android SDK 34. Execute `assembleRelease` e depois alinhe e assine o APK.

Referências: [ADB reverse](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server), [Android Debug Bridge](https://developer.android.com/tools/adb) e [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
