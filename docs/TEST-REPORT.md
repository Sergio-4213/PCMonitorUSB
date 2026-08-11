# Relatório de testes — PC Monitor USB 2.0.2

Data: 11/08/2026
Ambiente principal: Windows 11 x64, AMD Ryzen 7 3800XT, MSI B550M PRO-VDH WIFI, AMD Radeon RX 7600, 32 GB RAM e Android 8.1 Go por USB.

## Resultado automatizado

`9/9` testes aprovados:

1. parser ADB distingue autorizado, não autorizado e conexão por rede;
2. seleção de sensores usa tipo, nome, prioridade e identificador;
3. GPU principal não mistura vídeo integrado e dedicado;
4. configuração normaliza porta e intervalo;
5. lista permitida nega comandos arbitrários;
6. API local publica sensores e configuração do PC e exige token nos comandos;
7. compatibilidade ADB genérica aceita Android físico por USB;
8. janela mantém o nome e o botão Salvar totalmente dentro da área visível;
9. LibreHardwareMonitor coleta no computador real.

Compilação Windows: aprovada, zero erros e zero avisos.
Compilação Android release: aprovada.
Assinaturas APK v1, v2 e v3: verificadas.
Instalação do APK 2.0.0 no aparelho real: aprovada.

## Leituras reais verificadas

O servidor elevado publicou, durante o teste:

- CPU identificada: AMD Ryzen 7 3800XT;
- temperatura da CPU: 62,6 °C;
- clock da CPU: 4,33 GHz;
- potência da CPU: 68,3 W;
- GPU identificada: AMD Radeon RX 7600;
- temperatura da GPU: 55 °C;
- hotspot: 60 °C;
- clock da GPU: 2093 MHz;
- VRAM: 2,28 / 7,98 GB;
- potência da GPU: 50 W;
- RAM: 14,91 / 31,92 GB.

Esses números são um instantâneo e variam com a carga. Nenhum valor foi inventado.

## Revisão funcional 2.0

- Nome e pacotes visíveis migrados para **PC Monitor USB**.
- Novo ícone sem texto ou referência a modelo de celular aplicado ao EXE e ao APK.
- Visão geral reduzida a um botão: **Configurar celular**.
- Comandos iniciar/parar servidor removidos da bandeja.
- Botão da Visão geral alterna entre **Ligar servidor** e **Desligar servidor**, permanecendo clicável nos dois estados.
- Cabeçalho ampliado para escalas DPI do Windows e rodapé de configurações fixo, mantendo **Salvar configurações** sempre visível.
- Celular mostrado somente como conectado ou desconectado; o modelo não aparece na interface.
- Configuração exata do PC adicionada à visão geral e ao endpoint `/api/system`.
- Seleção de GPU baseada em identificador físico, capacidade de sensores e desempate determinístico.
- Minimização continua removendo o aplicativo da barra de tarefas e enviando-o à área de notificação.
- Migração preserva configuração, logs e Platform-Tools da versão anterior.
- Aplicativo Android anterior só é removido após validar e iniciar o pacote 2.0.

## Limites verificados

- O teste automatizado executado sem elevação não acessou a temperatura da CPU; o servidor elevado leu corretamente temperatura, clock e potência. Por isso o manifesto mantém solicitação de administrador.
- Nenhuma reinicialização foi executada.
- FPS continua `null` por não haver fonte PresentMon integrada.
- A primeira autorização RSA continua dependendo da confirmação física no Android.
