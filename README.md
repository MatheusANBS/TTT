# TTT

TTT é um aplicativo desktop para Windows focado em inspeção e manipulação de memória de processos locais. O projeto oferece fluxo completo para anexar a um processo, escanear valores, manter uma lista de endereços com atualização em tempo real, congelar valores e localizar cadeias de ponteiros.

O aplicativo atual usa Avalonia UI sobre .NET 8 e roda apenas em Windows x64.

## Principais recursos

- Anexar e desconectar de processos locais.
- Filtrar e listar processos com janela.
- Executar `First Scan` e `Next Scan` para busca por valor exato, valor desconhecido, alterado, inalterado, aumentado e diminuído.
- Manter uma address list com leitura periódica dos valores.
- Editar valores e congelar endereços em tempo real.
- Organizar endereços por grupos.
- Configurar hotkeys globais por grupo.
- Encontrar pointer chains para um endereço alvo.
- Salvar e carregar configuração da sessão.
- Exportar dados auxiliares para arquivos locais.
- Gerar instalador Windows com Inno Setup.

## Stack

- .NET 8 (`net8.0-windows`)
- Avalonia 11
- CommunityToolkit.Mvvm
- Windows APIs via P/Invoke (`ReadProcessMemory`, `WriteProcessMemory`, `VirtualQueryEx`, `VirtualProtectEx`)
- Inno Setup 6 para empacotamento

## Requisitos

- Windows 10 ou Windows 11
- Arquitetura x64
- SDK do .NET 8 instalado para desenvolvimento
- PowerShell 5.1+ para o script de build do instalador
- Inno Setup 6 instalado para gerar o setup

Observações:

- O projeto foi configurado para `x64`.
- Algumas operações podem exigir privilégios elevados, dependendo do processo alvo.
- Processos protegidos ou com anti-cheat podem falhar ao abrir handle.

## Estrutura do projeto

```text
TTT.Migration.sln           Solução principal
TTT/                        Aplicação desktop
  Services/                 Regras de negócio e acesso à memória
  ViewModels/               Orquestração MVVM
  Views/                    Telas Avalonia
  Models/                   Modelos de dados
build-installer.ps1         Publicação e geração do instalador
installer.iss               Script do Inno Setup
```

## Componentes principais

### Services

- `MemoryService`: abre o processo, enumera regiões de memória e realiza leitura/escrita.
- `ScannerService`: executa scans paralelos e refinamentos sobre resultados anteriores.
- `PointerMapperService`: busca cadeias de ponteiros para um endereço alvo.
- `ConfigService`: salva/carrega configurações e estado em JSON.
- `LogService`: centraliza logs do aplicativo.

### ViewModels

- `MainViewModel`: navegação, ciclo de vida e persistência da configuração.
- `ProcessViewModel`: seleção e conexão com processos.
- `ScannerViewModel`: scanner de memória e paginação de resultados.
- `AddressListViewModel`: lista de endereços, freeze, grupos e hotkeys.
- `PointerMapperViewModel`: busca, filtragem e validação de ponteiros.
- `LogViewModel`: exibição de logs na interface.

## Como executar em desenvolvimento

Na raiz do repositório:

```powershell
dotnet restore .\TTT.Migration.sln
dotnet build .\TTT.Migration.sln -c Debug
dotnet run --project .\TTT\TTT.csproj
```

Se quiser forçar a plataforma x64 no build:

```powershell
dotnet build .\TTT\TTT.csproj -c Debug -p:Platform=x64
```

## Publicação

Para publicar a aplicação self-contained para Windows x64:

```powershell
dotnet publish .\TTT\TTT.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -p:Platform=x64
```

Saída esperada:

```text
TTT\bin\x64\Release\net8.0-windows\win-x64\publish
```

## Gerar instalador

O script `build-installer.ps1` executa dois passos:

1. Publica o app em modo Release self-contained.
2. Compila o instalador via `ISCC.exe`.

Execução padrão:

```powershell
.\build-installer.ps1
```

Exemplo informando manualmente o caminho do Inno Setup:

```powershell
.\build-installer.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Artefatos gerados:

- Publicação: `TTT\bin\x64\Release\net8.0-windows\win-x64\publish`
- Instalador: `installer_output\TTT_Setup_2.0.0.exe` ou nome equivalente com a versão atual

## Arquivos gerados em runtime

Ao executar a aplicação, arquivos e diretórios auxiliares podem ser criados ao lado do executável:

- `appsettings.json`: estado simples da aplicação, como último arquivo de configuração.
- `logs.txt`: log do aplicativo.
- `configs\`: configurações salvas da sessão.
- `exports\`: arquivos exportados.
- `pointer-scans\`: snapshots de cadeias de ponteiro.

## Fluxo de uso

1. Abra a aba de processos e anexe ao alvo.
2. Vá para o scanner e execute o primeiro scan.
3. Refine os resultados com `Next Scan`.
4. Envie endereços relevantes para a address list.
5. Edite, congele ou agrupe entradas.
6. Use o pointer mapper para localizar cadeias persistentes.
7. Salve a configuração para reaproveitar a sessão depois.

## Limitações e cuidados

- O aplicativo é Windows-only por depender de APIs nativas do sistema.
- O processo alvo precisa ser acessível ao usuário atual ou executado com permissões compatíveis.
- A estabilidade de pointer chains depende do layout de memória do processo analisado.
- Alterações de memória em processos terceiros podem causar comportamento indefinido no alvo.

## Solução e projeto principal

- Solução: `TTT.Migration.sln`
- Projeto desktop: `TTT\TTT.csproj`
