# Ambiente (tarefa 0.1)

Registado a 2026-10-21.

| Item | Valor |
|---|---|
| .NET SDK | 10.0.203 |
| MonoGame | `MonoGame.Framework.WindowsDX` 3.8.4.1 |
| mgcb | `dotnet exec %USERPROFILE%\.nuget\packages\dotnet-mgcb\3.8.4.1\tools\net8.0\any\mgcb.dll` (escolhido por `AssetImporter.LocateMgcb`; também existe `dotnet-mgcb` 3.8.4 na cache NuGet, a mais alta vence) |
| Python | 3.14.4 (`py -3`) |
| numpy / Pillow | 2.4.6 / 12.2.0 (instalados no Python do sistema) |
| venv | `Tools/TexGen/.venv`, criado com `--system-site-packages` para reaproveitar numpy/Pillow sem rede |
| GPU | NVIDIA GeForce RTX 3060, 12 GB (os backends GPU da fase E são possíveis, falta `torch`/`diffusers` e os pesos) |

## Notas

* `requirements-core.txt` fixa numpy e Pillow; num ambiente limpo: `py -3 -m venv Tools\TexGen\.venv` e
  `Tools\TexGen\.venv\Scripts\pip install -r Tools\TexGen\requirements-core.txt`.
* `.gitignore` ignora `Tools/TexGen/{models,third_party,cache,.venv}/`, `Tools/TexGen/texgen.json` e `__pycache__/`.
* Testes Python: `Tools\TexGen\.venv\Scripts\python -m unittest discover Tools/TexGen/tests` (a partir da raiz, com `-t Tools/TexGen` se o import falhar).
