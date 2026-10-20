# Plano de Implementação v4: Materiais PBR estilo Source 2 gerados por IA (Obsidian)
### Versão para execução por agente (Claude / Codex), adaptada ao repositório atual

> **Como usar este documento.** É um plano executável: cada tarefa tem objetivo, ficheiros, especificação, verificação e critério de conclusão. O agente executa **uma tarefa de cada vez, por ordem**, e só avança quando a verificação passa. **[HUMANO]** = precisa de uma pessoa (decisões, downloads, aprovação visual). **[GPU]** = precisa de GPU NVIDIA com CUDA.
>
> **Objetivo:** gerar dinamicamente, a partir de um prompt (ou de uma fotografia), conjuntos de texturas PBR tileable com o aspeto dos materiais de Source 2 / Half-Life: Alyx, e entregá-los como **material assets (`.mat`) normais do Obsidian**, que se arrastam para gameobjects no Anvil e vão nos builds.

---

## 0. Alterações face à v3 (e porquê)

A v3 foi escrita para um motor MonoGame vazio. O Obsidian já tem quase tudo o que a v3 mandava construir, e as convenções dele são outras. Resumo:

| v3 | Realidade no Obsidian | v4 |
|---|---|---|
| Nova solução `engine/Engine.sln` com `Engine.Core`, `Engine.Tests`, `Engine.Tools`, `Game.WindowsDX` | Já existem `Engine`, `Editor/Anvil`, `Tests/Components`, `Tools/` | Código novo entra nos projetos existentes. Nenhum projeto C# novo. |
| MonoGame 3.8.5, migração para Vulkan | `MonoGame.Framework.WindowsDX` **3.8.4.1**, deferred renderer em DX11 | Vulkan **fora de âmbito** (antiga fase D removida). Sem atualização de MonoGame. |
| `Pbr.fx` + `Tonemap.fx` próprios | `Gbuffer.fx` + iluminação deferred + tonemap Source 2 (`PostProcessSettings`) já implementados | Nada de shaders novos. As texturas geradas alimentam o pipeline existente. |
| Baker de IBL em CPU, formato `.ibl` | `EnvironmentPrefilter` (cubos GGX + BRDF LUT), Reflection Probes, Probe Volumes, lightmaps | Removido. |
| `VertexPositionNormalTangentTexture`, `MeshBuilder` | Os modelos já trazem tangentes/binormais do content pipeline; existem `Assets.IsoSphere`, planos, cubos | Removido. |
| Cena `MaterialLab` com screenshot próprio | `LookDevCapture` (`Engine.exe --scene ... --shot out.png`) + `LookDev.obsc` gerado por Python | Nova cena `MaterialLab.obsc`, capturada com o `LookDevCapture` existente. |
| Mapa **ARM** empacotado (R=AO, G=Rough, B=Metal) | `MaterialComponent` tem slots **separados** (`RoughnessTexture`, `MetallicTexture`, `AmbientOcclusionTexture`), cada um lido do canal **.r** | Mapas separados, em tons de cinzento. Sem ARM. |
| Normal **OpenGL / Y+** | `Gbuffer.fx` (`GetNormalMap`, linha 223) faz `rgb - 0.5` sem inverter o verde. As texturas Poly Haven em `GameObjects/Textures/Ground` são **DirectX** e estão corretas | Convenção **DirectX / Y−**, confirmada na tarefa 0.2 antes de ser fixada. |
| Texturas carregadas em runtime com `FromStream` + mips em CPU | O motor carrega texturas **só como `.xnb`** (`MaterialComponent.ApplyTexture` → `content.Load<Texture2D>`). Os builds só levam os `.xnb` | Caminho principal: PNG → `Content.mgcb` → mgcb → `.xnb`, como o `AssetImporter.ImportTexture` já faz. Carregamento em runtime fica como fase opcional (F). |
| Height em PNG 16-bit | O mgcb converte para `TextureFormat=Color` (8-bit). O slot é `DisplacementTexture` (parallax / poças do WetSurface) | Height em PNG **8-bit L** no Content. A versão 16-bit fica apenas na pasta de depuração. |
| `docs/PLAN.md`, `docs/PROGRESS.md`, novo `CLAUDE.md` | O `CLAUDE.md` do projeto já tem regras próprias (changelog, Engine_Wiring, versão, ISSUES) | O plano vive em `Docs/Research/AiTextures/`. O `CLAUDE.md` existente **não é substituído**; só ganha uma entrada no Repo Map no fim. |
| Gerador sem noção de "estilo" | O objetivo é o look Source 2 | Novos **perfis de estilo** (`source2`) com regras de valores PBR, prompts e validação (secção 4). |

Mantém-se da v3: gerador Python local em CLI com JSON lines, sem rede, backends `fake` / `classic` / `sdxl_classic` / `sdxl_chord`, ID determinístico, cache, seam check, escrita atómica, CHORD opcional por causa da licença.

---

## 1. Contexto técnico (factos verificados no repositório)

* **Material:** `Engine/Components/MaterialComponent.cs` tem 7 slots de textura (caminhos relativos ao Content, **com** extensão; o runtime remove-a): `BaseColorTexture`, `NormalTexture`, `RoughnessTexture`, `MetallicTexture`, `MaskTexture`, `DisplacementTexture`, `AmbientOcclusionTexture`.
  * `null` herda o mapa do modelo; `""` remove o mapa.
  * Escalares relevantes: tint `Red/Green/Blue`, `Roughness`, `Metallic`, `TilingU/V`, `MaterialType` (Basic 0 … WetSurface 7, Foliage 8).
  * Não há textura emissiva (só `EmissiveStrength`) nem tint mask.
* **Shader** (`Content/Shaders/GBufferSetup/Gbuffer.fx`):
  * Normal: `NormalMap.rgb - 0.5` multiplicado por `WorldToTangentSpace` (Tangent, Binormal, Normal do vértice).
  * Roughness, metallic e AO lidos de `.r`. A roughness é **perceptual** (não gloss).
  * A AO entra no alpha do albedo e só escurece a **luz indireta** (como em HLA).
* **Albedo:** textura `Color` (sem formato sRGB). O shader descodifica com `pow(2.2)` (`DeferredCompose.fx`, `helper.fx` `AlbedoToLinear`). O albedo é **escrito em sRGB**; os restantes mapas em linear.
* **F0:** `helper.fx` `GetF0 = lerp(0.04, albedo, metalness)` (metais tingidos, item 1.1 do Source2Look). O albedo de um metal **é** a sua cor F0.
* **Import de texturas:**
  * `AssetImporter.ImportTexture` (`Engine/Recources/AssetImporter.cs:228`) copia para `GameObjects/Textures/`, acrescenta a entrada ao `Content.mgcb`, corre o mgcb (`RunMgcbBuild`, `LocateMgcb`), copia o `.xnb` e carrega-o.
  * `ContentManifest.Register` escreve entradas com `ColorKeyEnabled=True` (o magenta puro fica transparente) e `PremultiplyAlpha=True`. Os materiais gerados precisam de `ColorKeyEnabled=False` e `PremultiplyAlpha=False`.
  * O `ContentManager` guarda texturas em cache pelo nome. Regenerar com o **mesmo** caminho não atualiza a textura em memória. Por isso o caminho inclui o ID (secção 2.2).
  * Os mips do mgcb são box filter sobre os valores **sem** linearizar e sem renormalizar normais. É aceitável para a v4; a melhoria fica na fase F.
* **Material assets:** `Engine/Recources/MaterialAsset.cs`.
  * Formato `{ "Version": 1, "Material": { ...MaterialComponent... } }`, escrita atómica.
  * `SyncScene` recarrega `.mat` alterados, `Assign(entity, "Nome")` liga um material a um gameobject, `SourceRootOverride` serve de gancho de testes.
* **Testes:** `Tests/Components` (sem framework).
  * Cada suite é uma classe `internal static class XxxChecks { public static void Run() }` registada em `Program.cs`, que usa `Check(bool, "msg")`.
  * Modelo a seguir: `MaterialAssetChecks.cs`. Para GPU: `MaterialGraphicsChecks.cs` (`--graphics`).
* **Captura visual:** `Engine\bin\Debug\net10.0-windows\Engine.exe --scene Scenes/X.obsc --shot out.png [--camera px,py,pz,tx,ty,tz] [--frames N] [--size WxH] [--rendermode M] [--bake]` (`Engine/Logic/LookDevCapture.cs`). Cenas de teste são geradas por Python (`Docs/Research/Source2Look/lookdev/generate_lookdev.py`; Z para cima, metros).
* **Project reset:** `ProjectReset.KeptPaths` não inclui `Materials/` nem `GameObjects/Textures/`. Materiais gerados são conteúdo do projeto e são apagados com Delete Project. É o comportamento correto.
* **CHORD** (Ubisoft La Forge): licença *Research-Only – Copyleft*; uso comercial provavelmente proibido. Ótimo a 1024². Fica opcional e atrás de interface.

---

## 2. Contratos fixos (só mudam com aprovação humana)

### 2.1 Convenções de dados

| Item | Valor fixo |
|---|---|
| Convenção do normal map | **DirectX / Y−** (igual às texturas Poly Haven já em uso). *Fixado só depois da tarefa 0.2.* O gerador trabalha internamente em Y+ e converte na escrita. |
| Codificação do normal | `rgb = n * 0.5 + 0.5`, PNG RGB 8-bit, vetor normalizado, `z > 0` |
| Albedo (`BaseColorTexture`) | PNG RGB 8-bit, **sRGB**, sem iluminação nem AO "cozida" |
| Roughness / Metallic / AO | **três** PNG 8-bit em tons de cinzento (`L`), lineares, lidos de `.r` |
| Height (`DisplacementTexture`) | PNG 8-bit `L`, linear, 0 = fundo, 255 = topo. A versão 16-bit fica só em `_debug/` |
| Roughness | clamp a `[0.04, 1.0]` |
| Metalness | binário por defeito (0 ou 1; transições só em orlas de 1–2 px) |
| Resolução | quadrada, potência de 2; por defeito 1024 (512 / 2048 permitidos) |
| Origem UV | canto superior esquerdo |
| Cor proibida | nenhum pixel do albedo pode ser exatamente (255, 0, 255). É uma salvaguarda, mesmo com `ColorKeyEnabled=False`. |

### 2.2 Onde ficam os ficheiros

```
Engine/Content/Materials/Generated/<slug>_<id>.mat            # o material asset (o que o utilizador usa)
Engine/Content/Materials/Generated/<slug>_<id>/
  <slug>_BaseColor.png
  <slug>_Normal.png
  <slug>_Roughness.png
  <slug>_Metallic.png        # omitido se todo preto (o .mat usa Metallic = 0)
  <slug>_AO.png
  <slug>_Height.png
  texgen.json                # metadados (2.3); não vai para o mgcb
Tools/TexGen/cache/<id>/     # saída bruta do gerador (fora do git): os PNG acima + _debug/
  _debug/source_rgb.png, height16.png, validation.json
```

* `slug` = prompt em minúsculas, `[a-z0-9_]`, máximo 32 caracteres. Exemplo: `mossy_cobblestone`.
* O `id` no caminho evita a cache do `ContentManager` ao regenerar.
* Só o conteúdo de `Engine/Content/...` entra no `Content.mgcb` e no git. O `Tools/TexGen/cache/` fica ignorado.

### 2.3 `texgen.json` (schema v1)

```json
{
  "schema": 1,
  "id": "3fa1c09b7d2e4a55",
  "params": {
    "prompt": "mossy cobblestone",
    "negative_prompt": "",
    "seed": 1234,
    "resolution": 1024,
    "backend": "fake",
    "style": "source2",
    "family": "stone",
    "force_dielectric": false,
    "input_rgb": null
  },
  "pipeline_version": "1",
  "normal_convention": "directx",
  "colorspace": { "basecolor": "srgb", "normal": "linear", "roughness": "linear", "metallic": "linear", "ao": "linear", "height": "linear" },
  "seam_score": 0.0123,
  "validation": { "passed": true, "warnings": [] },
  "suggested": { "tiling": 1.0, "material_type": "Basic" },
  "created_utc": "2026-10-20T13:30:00Z"
}
```

### 2.4 ID do material (calculado em C#)

```
canonical = "v{pipeline_version}|{backend}|{style}|{family}|{resolution}|{seed}|{force_dielectric ? 1 : 0}|{input_rgb_sha256 or ''}|{prompt}|{negative_prompt}"
id        = primeiros 16 caracteres hexadecimais minúsculos de SHA-256(UTF-8(canonical))
```

* `prompt` e `negative_prompt` são usados após `Trim()`.
* `pipeline_version` é uma constante em C# (`"1"`). Muda quando mudam os modelos, o pós-processamento ou os perfis de estilo, e assim invalida a cache.

### 2.5 CLI do gerador

```
python -m texgen generate --prompt TEXT --seed INT --out DIR
                          [--negative-prompt TEXT] [--resolution 1024]
                          [--backend fake|classic|sdxl_classic|sdxl_chord]
                          [--style source2] [--family auto|stone|concrete|brick|tile|plaster|wood|metal|painted_metal|fabric|ground|organic]
                          [--input-rgb PATH] [--force-dielectric]
                          [--normal-convention directx|opengl] [--no-seam-check]
                          [--pipeline-version 1]
python -m texgen check     [--backend NAME]
python -m texgen styles    # lista perfis e famílias em JSON
python -m texgen --version
```

| Backend | Texto → RGB | RGB → PBR | GPU |
|---|---|---|---|
| `fake` | ruído procedural tileable | estimador clássico | não |
| `classic` | requer `--input-rgb` (fotografia / textura existente) | estimador clássico | não |
| `sdxl_classic` | difusão tileable | estimador clássico | sim |
| `sdxl_chord` | difusão tileable | CHORD | sim |

**Protocolo stdout:** uma linha JSON por evento; nada mais vai para o stdout (os logs vão para o stderr).

```json
{"event":"progress","stage":"diffusion","progress":0.35}
{"event":"warning","code":"albedo_too_dark","message":"..."}
{"event":"done","out":"C:/.../Tools/TexGen/cache/3fa1c09b7d2e4a55"}
{"event":"error","code":"model_missing","message":"..."}
```

* Estágios: `load`, `diffusion`, `estimation`, `style`, `postprocess`, `validate`, `write`.
* **Códigos de saída:** `0` sucesso · `1` erro interno · `2` argumentos inválidos · `3` modelo/dependência em falta · `4` seam check falhou · `5` validação de estilo falhou (só com `--strict`).
* Escrita atómica: escreve em `DIR.tmp` e renomeia para `DIR` no fim.

---

## 3. Estrutura no repositório

```text
obsidian/
├── Docs/Research/AiTextures/
│   ├── PLAN.md                          # este documento (movido da raiz na tarefa 0.1)
│   ├── PROGRESS.md                      # checklist atualizada pelo agente
│   └── ENVIRONMENT.md                   # tarefa 0.1
├── Docs/markdown/Generated_Materials.md # documentação de utilizador (tarefa C4)
├── Tools/TexGen/                        # Python, nunca é dependência de build do C#
│   ├── requirements-core.txt            # numpy, pillow (versões fixadas)
│   ├── requirements-gpu.txt             # torch, diffusers, transformers, accelerate, safetensors
│   ├── texgen.example.json              # configuração (copiada para texgen.json local, fora do git)
│   ├── texgen/
│   │   ├── __main__.py, cli.py, protocol.py, pipeline.py
│   │   ├── backends/ fake_rgb.py, classic_pbr.py, sdxl_rgb.py [GPU], chord_pbr.py [GPU, opcional]
│   │   ├── styles/ source2.json, style.py   # perfis de estilo (secção 4)
│   │   ├── postprocess.py, seams.py, validate.py
│   ├── lab/generate_material_lab.py     # gera Content/Scenes/MaterialLab.obsc
│   ├── tests/                           # unittest (stdlib)
│   ├── models/                          # pesos (fora do git)
│   ├── third_party/                     # clone do CHORD se aprovado (fora do git)
│   └── cache/                           # saídas brutas (fora do git)
├── Engine/Recources/TextureGeneration/  # C#, mesmo estilo do resto de Engine/Recources
│   ├── TexGenSettings.cs                # lê Tools/TexGen/texgen.json
│   ├── MaterialGenerationId.cs          # secção 2.4
│   ├── TexGenProtocol.cs                # parse das JSON lines
│   ├── TexGenRunner.cs                  # processo Python local
│   └── GeneratedMaterialImporter.cs     # cache → Content → mgcb → .mat
├── Editor/Anvil/ ... (tarefas C1–C3)
└── Tests/Components/TexGenChecks.cs     # testes C# (registados em Program.cs)
```

**Regras:**
* Código C# novo segue o estilo de `Engine/Recources` (C# antigo, nullable desligado) ou de `Anvil` (nullable + MVVM).
* Sem pacotes NuGet novos.
* Mutações de cena só no game thread, através do `IEditorBridge`.
* Os caminhos do Content são sem extensão no `content.Load` e com extensão nos campos do `MaterialComponent`.

---

## 4. Perfil de estilo `source2` (o que torna o resultado "Source 2")

O motor já reproduz a iluminação, o tonemap e a BRDF de HLA (`Docs/Research/Source2Look/README.md`). O que falta é que as **texturas** sigam a disciplina de autoria desse jogo. As regras abaixo são objetivos de aparência, não valores extraídos dos ficheiros da Valve; afinam-se nos checkpoints visuais.

1. **Albedo sem luz:** delighting obrigatório, sem sombras de contacto nem AO no albedo (a AO vai para o seu próprio mapa e só afeta a luz indireta). Saturação moderada e cores levemente "sujas", sem tons puros.
2. **Intervalos PBR** (validados por `validate.py`, em luminância sRGB do albedo):
   * dielétricos: média 50–220, nenhum pixel abaixo de 25 ou acima de 245 em mais de 0,5 % da área;
   * metais (metalness = 1): albedo = cor F0 real, luminância 140–250 (ferro/aço cinzento-azulado, cobre/latão/ouro saturados);
   * roughness: nunca constante (desvio-padrão ≥ 0,03); intervalo típico por família (tabela abaixo).
3. **Metais de Source 2 quase sempre pintados ou gastos:** a família `painted_metal` gera uma máscara de desgaste a partir da height/curvatura: na tinta fica dielétrico com roughness alta, no metal à vista fica metalness = 1 com roughness baixa.
4. **Normal com duas escalas:** forma (derivada da height) + micro-detalhe (do albedo em alta frequência), somadas por *reoriented normal mapping*. Força por família.
5. **AO suave:** calculada da height com raio curto. Mínimo 0,35, porque escurece só a luz indireta.
6. **Tileable a 1–2 m:** cada família tem uma escala real sugerida (`suggested.tiling` para um plano de 1 m).

| Família | Sufixo de prompt (diffusion) | Roughness | Metal | Força normal | Escala (m/tile) |
|---|---|---|---|---|---|
| `stone`, `brick`, `concrete`, `tile`, `plaster` | `", photographed surface, worn, realistic, subtle grime"` | 0.55–0.95 | 0 | 2.0–3.0 | 1–2 |
| `wood` | `", aged wood, realistic grain"` | 0.45–0.85 | 0 | 1.5 | 1 |
| `metal` | `", bare metal, scratches, realistic"` | 0.20–0.60 | 1 | 1.0 | 0.5–1 |
| `painted_metal` | `", chipped paint over metal, industrial"` | tinta 0.5–0.8, metal 0.2–0.4 | máscara | 1.5 | 1 |
| `fabric`, `ground`, `organic` | `", realistic, natural"` | 0.70–1.00 | 0 | 2.0 | 1–2 |

* Sufixo comum: `", seamless tileable texture, top-down orthographic, flat even diffuse lighting, no shadows, no perspective"`.
* Negative comum: `"perspective, shadows, highlights, specular reflections, text, watermark, vignette, cartoon"`.
* `--family auto`: palavras-chave do prompt → família; se nenhuma corresponder, `stone`.
* `style.py` aplica a família depois da estimação: clamp/remapeamento dos intervalos, correção de saturação e luminância do albedo, máscara de metal. Também preenche `suggested`.
* O perfil vive em `styles/source2.json`, para que outros estilos (`default`, mais tarde) possam ser acrescentados sem tocar no código.

---

## 5. Fase 0 — Inventário e decisões

### 0.1 · Inventário do ambiente
* **Fazer:**
  * mover este ficheiro para `Docs/Research/AiTextures/PLAN.md` e criar `PROGRESS.md`;
  * registar em `ENVIRONMENT.md`: versão do .NET SDK, MonoGame 3.8.4.1, comando que corre o mgcb (`AssetImporter.LocateMgcb`), versão do Python disponível (`py -3 --version`), presença de GPU/CUDA (`nvidia-smi`);
  * acrescentar `Tools/TexGen/{models,third_party,cache,.venv}/` e `Tools/TexGen/texgen.json` ao `.gitignore`.
* **Não fazer:** alterar código.
* **Concluído quando:** os três ficheiros existem e o `.gitignore` foi atualizado.

### 0.2 · Confirmar a convenção do normal map
* **Fazer:** gerar `Content/Scenes/NormalConventionTest.obsc` com dois planos lado a lado: um com o normal map Poly Haven `Bricks_Normal.jpg` (DirectX) e outro com o mesmo mapa com o verde invertido. Uma luz pontual baixa de um dos lados. Capturar com `LookDevCapture` com a luz em duas posições (`--camera` fixo).
* **Verificação pelo agente:** no plano correto, as juntas de tijolo ficam escuras do lado oposto à luz, em ambas as posições, e o plano invertido mostra relevo "afundado".
* **[HUMANO] Checkpoint:** confirmar qual dos dois está certo. O resultado fixa a linha "Convenção do normal map" em 2.1.
* **Nota:** se o resultado for Y+, só muda o valor por defeito de `--normal-convention`. O plano mantém-se.

### 0.3 · Formato de entrada do mgcb para materiais gerados
* **Fazer:**
  * acrescentar a `ContentManifest.Register` (ou a um método irmão) a opção de escrever entradas de textura com `ColorKeyEnabled=False`, `PremultiplyAlpha=False` e `GenerateMipmaps=True`, sem mudar o comportamento atual do import do Anvil;
  * confirmar que o mgcb aceita PNG 8-bit em tons de cinzento (`L`) e que o `.r` chega ao shader com o valor certo.
* **Verificação:** check em `Tests/Components/TexGenChecks.cs` (com `--graphics`): um PNG `L` com valor 128 importado → `GetData` dá R = 128.

---

## 6. Fase A — Gerador `texgen` (Python)

Regras desta fase:
* testes com `unittest` da biblioteca padrão;
* `torch`/`diffusers` importados **só** dentro das funções dos backends GPU;
* nenhum acesso à rede (`HF_HUB_OFFLINE=1`, `local_files_only=True`);
* núcleo só com stdlib + `numpy` + `Pillow`, sem OpenCV nem SciPy;
* o Python corre num venv em `Tools/TexGen/.venv`.

### A0 · Esqueleto do pacote
* `requirements-core.txt`, `requirements-gpu.txt`, `texgen/__main__.py`, `cli.py` (argparse), `protocol.py` (`emit_progress/warning/done/error` → `print(json.dumps(...), flush=True)`).
* **Verificação:** `python -m texgen --version` e `python -m unittest discover Tools/TexGen/tests`.

### A1 · Backend `fake` e pipeline ponta a ponta
* `fake_rgb.generate(prompt, seed, size)`: ruído de valor **periódico** (tileable por construção), cor derivada do hash do prompt, com o tom de base da família (pedra cinzenta, madeira castanha, …).
* `pipeline.run(params, out_dir)`: orquestra os estágios, emite eventos, faz escrita atómica e escreve os 6 PNG + `texgen.json` + `_debug/`.
* **Testes:** CLI via `subprocess` com `--backend fake --resolution 256`. Validar:
  * nomes dos ficheiros (2.2) e `texgen.json` (2.3);
  * modos PNG (`RGB` / `L`);
  * todas as linhas do stdout são JSON válido;
  * exit 0; argumentos inválidos → exit 2.

### A2 · Estimador PBR clássico (`classic_pbr.py`)
* **Albedo:** `rgb_lin / (blur_gauss_wrap(lum, sigma=size/16) / mean(lum))`, clamp, de volta a sRGB (delighting).
* **Height:** luminância com blur leve (`sigma=1.5`), normalizada para [0,1]; opcionalmente mistura com uma versão de baixa frequência para a forma.
* **Normal:** Sobel com **wrap** sobre a height (forma) + Sobel sobre o detalhe de alta frequência do albedo (micro), combinados por RNM. Trabalha em Y+ internamente.
* **Roughness:** `1 − contraste local normalizado`, depois remapeado pela família (secção 4).
* **Metalness:** zeros (o perfil de estilo pode substituir).
* Filtros com numpy (FFT ou `np.roll`).
* **Testes:** formas e intervalos certos; entrada tileable dá mapas tileable (seam score baixo).

### A3 · Pós-processamento (`postprocess.py`)
* `sanitize_normal` (renormalizar, `z>0`, conversão para a convenção de saída invertendo o Y);
* `clamp_roughness`, `binarize_metal(threshold=0.5)` + `force_dielectric`;
* `ao_from_height` (8 direções, raio `size/64`, wrap, mínimo 0,35);
* `remove_magenta(albedo)`;
* escrita PNG (`RGB` / `L`; `height16.png` `I;16` só em `_debug/`).
* **Testes:**
  * height plana dá AO = 1;
  * a normal (0,0,1) codifica para (128,128,255);
  * a conversão DX↔GL ida e volta dá o mesmo resultado;
  * nenhum pixel (255,0,255) à saída.

### A4 · Perfil de estilo e validação (`styles/`, `validate.py`)
* Implementar a secção 4: carregar `source2.json`, `--family auto`, ajustes pós-estimação, `suggested`.
* `validate.py` mede os intervalos da secção 4.2 e emite `warning` por regra violada (`albedo_too_dark`, `albedo_too_bright`, `roughness_flat`, `metal_albedo_dark`, …). Com `--strict`, sai com o código 5.
* **Testes:**
  * imagem preta → `albedo_too_dark`;
  * `painted_metal` produz metalness com 0 e 1 e roughness mais baixa onde o metal fica à vista;
  * `auto` com "rusty steel plate" → `metal` ou `painted_metal`.

### A5 · Verificação de seams (`seams.py`)
* `seam_score(img)`: `np.roll` por metade em X e Y; média do gradiente absoluto nas linhas e colunas centrais, dividida pela média do gradiente global.
* Passa se `score < 1.5`. O score vai para o `texgen.json`. Se falhar, exit 4, exceto com `--no-seam-check`.
* **Testes:** ruído periódico passa; gradiente linear falha.

### A6 · `texgen check` e `texgen styles`
* `check`: dependências do backend importáveis, modelos presentes em `Tools/TexGen/models/`, CUDA para backends GPU. Emite um único `done` ou `error` (`model_missing` / `dependency_missing` / `cuda_unavailable`).
* `styles`: lista perfis e famílias (o Anvil usa isto para preencher o diálogo).

---

## 7. Fase B — Integração no motor (C#)

### B1 · `MaterialGenerationId`, `TexGenProtocol`, `TexGenSettings`
* `MaterialGenerationId.Compute(TexGenParams p, string pipelineVersion)` conforme 2.4.
* `TexGenProtocol.Parse(string line)` → `Progress` / `Warning` / `Done` / `Error` / `Unknown`, com `System.Text.Json`.
* `TexGenSettings`: lê `Tools/TexGen/texgen.json` (`pythonExe` = venv por defeito, `texgenRoot`, `cacheRoot`, `defaultBackend`, `timeoutSeconds` = 600). Um ficheiro em falta dá valores por defeito, não um erro.
* **Testes** (`TexGenChecks.cs`):
  * vetores de ID fixos (SHA-256 esperado calculado com `python -c "import hashlib; ..."` e escrito no teste);
  * parsing das 5 variantes e de lixo.

### B2 · `TexGenRunner` (processo local)
* `Task<TexGenResult> GenerateAsync(TexGenParams p, IProgress<TexGenEvent> progress, CancellationToken ct)`:
  * `ProcessStartInfo` com `UseShellExecute=false`, stdout/stderr redirecionados, `WorkingDirectory = texgenRoot`, `HF_HUB_OFFLINE=1`;
  * lê o stdout linha a linha e envia o stderr para a consola do motor;
  * cancelamento ou timeout → `Process.Kill(entireProcessTree: true)`;
  * valida o `texgen.json` no fim.
* Se `cacheRoot/<id>` já for válido (schema 1, 6 PNG ou 5 sem Metallic), devolve-o sem correr o Python.
* Corre **fora** do game thread.
* **Teste de integração** (ignorado se `pythonExe` não existir): `--backend fake --resolution 128` para uma pasta temporária.

### B3 · `GeneratedMaterialImporter` (cache → material asset)
* `Import(string cacheDir)` corre no **game thread** (como o `AssetImporter`), sob o `MgcbEditMutex` existente:
  1. copia os PNG para `Content/Materials/Generated/<slug>_<id>/`;
  2. regista-os no `Content.mgcb` com as opções da tarefa 0.3;
  3. corre o mgcb uma vez e copia os `.xnb` (reutiliza `RunMgcbBuild` / `CopyXnbForAsset`);
  4. escreve `Materials/Generated/<slug>_<id>.mat` com o `MaterialAsset` existente: slots preenchidos, `Roughness`/`Metallic` escalares neutros, `TilingU/V` = `suggested.tiling`, `MaterialType` sugerido;
  5. devolve o caminho do `.mat`.
* Se o mgcb falhar, desfaz as entradas do `Content.mgcb` e apaga a pasta copiada (o mesmo padrão de rollback do `AssetImporter`).
* **Testes:**
  * com `MaterialAsset.SourceRootOverride` numa pasta temporária, a importação de uma saída `fake` dá um `.mat` válido e entradas no mgcb com `ColorKeyEnabled=False`;
  * reimportar o mesmo ID não duplica entradas.

### B4 · Bridge
* `IEditorBridge.EnqueueImportGeneratedMaterial(string cacheDir, Action<string matPath, string error> done)` → `GeneratedMaterialImporter` no game thread.
* Opcional: `assignToId` para ligar logo o material ao gameobject selecionado (`MaterialAsset.Assign`).
* **Teste:** no `TexGenChecks` através do bridge, como as outras suites fazem.

---

## 8. Fase C — Anvil e MCP

### C1 · Diálogo "Generate Material"
* Assets > botão direito > **Generate Material...** e GameObject > **Generate Material for Selection...**.
* Campos:
  * prompt, negative prompt, seed (com botão aleatório), resolução;
  * backend (só os que `texgen check` dá como disponíveis);
  * família (de `texgen styles`), *Use photo...* (backend `classic` com `--input-rgb`), *Force dielectric*.
* O `TexGenRunner` corre num `Task` do Anvil. A barra de progresso mostra estágio + %. Os avisos de validação aparecem numa lista. Há um botão Cancel.
* No fim, `EnqueueImportGeneratedMaterial`. O novo `.mat` aparece no painel Assets, e é selecionado e aberto no Inspector (`MaterialAssetViewModel`).
* **Verificação:** com `--backend fake`, gerar "test stone" a partir do Anvil (skill `run-obsidian`) → o `.mat` aparece e, arrastado para um cubo, mostra as texturas.

### C2 · Pré-visualização e variações
* No diálogo: miniaturas dos PNG da cache (lidas como bitmap Avalonia, sem passar pelo motor) antes de importar.
* **Regenerate** (seed nova) e **Import**.
* Importar duas seeds do mesmo prompt dá dois `.mat` independentes.

### C3 · Ferramenta MCP `materials_generate`
* No servidor MCP do Anvil (`Editor/Anvil/Mcp/`): `materials_generate { prompt, seed?, family?, backend?, assignToId? }`.
* Devolve um `operationId`; o resultado chega por `editor_get_operation` (o mesmo padrão assíncrono e idempotente por `requestId`). No fim, devolve o caminho do `.mat` e os avisos.
* Atualizar `Docs/markdown/Editor_MCP.md`.

### C4 · Documentação
* `Docs/markdown/Generated_Materials.md`: instalação do venv, backends, famílias, perfil `source2`, onde ficam os ficheiros, limitações (mips do mgcb, licença do CHORD).
* Entrada no Repo Map e em Common Change Recipes do `CLAUDE.md`, `Engine_Wiring.md`, `CHANGELOG.md`, versão no `README.md` e `ISSUES.md`, conforme as regras do projeto.

---

## 9. Fase D — Validação visual (MaterialLab)

### D1 · Cena `MaterialLab.obsc`
* `Tools/TexGen/lab/generate_material_lab.py` (o mesmo estilo de `generate_lookdev.py`) gera `Content/Scenes/MaterialLab.obsc`. A cena tem:
  * um plano de chão 4×4 m, um cubo de 1 m e uma `IsoSphere` com o mesmo `.mat` (por `MaterialPath`);
  * uma esfera de referência dielétrica cinzenta (albedo 0.5 linear) e uma de metal liso;
  * sol baixo, reflection probe e luz pontual lateral (para ver o relevo).
* `LookDevCapture` ganha `--material <Materials/...mat>`, que aplica esse material aos gameobjects com o nome `MaterialLab.Target*` antes da captura.
* **Verificação pelo agente:** `Engine.exe --scene Scenes/MaterialLab.obsc --material Materials/Generated/<x>.mat --shot out/lab.png`. A imagem não tem NaN nem preto total, o chão mostra a textura repetida sem costuras visíveis e a esfera de referência serve de calibração de valores.

### D2 · [HUMANO + agente] Referência CC0 e calibração
* **Humano:** colocar 2–3 materiais CC0 de referência (um metálico) em `Tools/TexGen/reference/`.
* **Agente:**
  * passa cada referência pelo backend `classic` (`--input-rgb` = albedo da referência) e captura lado a lado com os mapas originais;
  * mede com `validate.py` as diferenças de intervalos;
  * propõe ajustes ao `source2.json` (sem os aplicar sem aprovação, porque mudar o perfil muda `pipeline_version`).
* **[HUMANO] Checkpoint visual:** relevo na direção certa, valores plausíveis, aspeto "Source 2" (comparar com capturas de HLA, se as houver).

---

## 10. Fase E — Backends GPU

### E1 · [GPU] Difusão tileable (`sdxl_rgb.py`)
* **[HUMANO] antes:** colocar os pesos em `Tools/TexGen/models/sdxl/` e instalar `requirements-gpu.txt` no venv. Pode ser outro modelo de difusão compatível com `diffusers`; o agente lê a documentação instalada e **não inventa APIs**.
* **Agente:**
  * carregar com `local_files_only=True` e `torch_dtype=float16`;
  * `make_tileable(module)` põe `padding_mode="circular"` em todas as `Conv2d` da UNet **e** do VAE;
  * scheduler DPM++ 2M, 25 steps;
  * prompt = prompt + sufixo da família + sufixo comum (secção 4); negative = negative comum + o do utilizador;
  * progresso por step via callback.
* **Teste sem GPU:** `make_tileable` num modelo minúsculo em CPU (`skipUnless(torch)`).
* **[HUMANO/GPU] verificação:** `python -m texgen generate --backend sdxl_classic --prompt "red brick wall" --seed 1 --out ...` → exit 0, seam score dentro do limite, validação sem avisos graves. Depois, MaterialLab.

### E2 · [GPU] [HUMANO: licença] CHORD (opcional)
* Só com aprovação explícita da licença. O humano clona o repositório para `Tools/TexGen/third_party/chord` e coloca os pesos.
* **Agente:**
  * lê o README e o código de inferência para descobrir a API real;
  * `chord_pbr.estimate(rgb) -> dict` com padding circular (1024 → 1280), inferência, recorte do centro e mapeamento para albedo/normal/height/roughness/metalness;
  * converte a normal do CHORD para Y+ interno (documentar a convenção encontrada);
  * a AO continua a vir de `ao_from_height`, e o perfil de estilo aplica-se depois do CHORD como no `classic`.
* **[HUMANO] checkpoint visual** no MaterialLab.

---

## 12. Regras para o agente (somam-se ao `CLAUDE.md` do projeto, não o substituem)

* Executa **uma** tarefa de cada vez, pela ordem. Corre a verificação da tarefa e só depois marca em `Docs/Research/AiTextures/PROGRESS.md`.
* **[HUMANO]**: prepara o que conseguires, escreve em `PROGRESS.md` o que falta e para.
* **[GPU]**: escreve o código e os testes de CPU. Sem CUDA, marca "aguarda GPU" e segue para a próxima tarefa independente.
* Os contratos da secção 2 são fixos. Se achares que estão errados, para e explica.
* Python núcleo: só stdlib + numpy + pillow. C#: sem NuGet novos.
* O código nunca acede à rede (downloads, HTTP, hubs de modelos).
* Quando uma API (MonoGame, mgcb, diffusers, CHORD) não estiver clara, lê o código ou a documentação instalada. Não inventes nomes.
* Não alteres testes para os fazer passar.
* Modelos de agentes: pesquisa e tarefas simples com Sonnet 5.5; decisões de arquitetura com Opus 5.5 (regra do `CLAUDE.md`).
* No fim de cada sessão: `CHANGELOG.md` (assinado), `Engine_Wiring.md`, versão MAJOR.MINOR.PATCH no `README.md` e `ISSUES.md`.
* Comandos:
  * Build: `dotnet build Engine.slnx /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign`
  * Testes C#: `dotnet run --project Tests\Components\Components.csproj` (+ `--graphics` para a tarefa 0.3)
  * Testes Py: `Tools\TexGen\.venv\Scripts\python -m unittest discover Tools/TexGen/tests`
  * Captura: `Engine\bin\Debug\net10.0-windows\Engine.exe --scene Scenes/MaterialLab.obsc --material <mat> --shot out\lab.png`

---

## 13. Ordem e dependências

```
0.1 → 0.2[H] → 0.3
A0 → A1 → A2 → A3 → A4 → A5 → A6                (em paralelo com 0.2/0.3)
0.3 + A6 → B1 → B2 → B3 → B4 → C1 → C2 → C3 → C4
B3 → D1 → D2[H]
A6 → E1[GPU,H] → E2[GPU,H,opcional]             (não bloqueiam B/C/D: testados com fake/classic)
F*  só por decisão humana
```

## 14. Riscos

| Risco | Mitigação |
|---|---|
| Convenção do normal errada (relevo invertido) | Tarefa 0.2 antes de fixar o contrato; conversão só na escrita; luz lateral no MaterialLab |
| Magenta tornado transparente pelo mgcb | Entradas com `ColorKeyEnabled=False` (0.3) + `remove_magenta` |
| Textura antiga em cache no `ContentManager` ao regenerar | ID no caminho; cada geração é um asset novo |
| Mips do mgcb sem linearização (albedo escurece ao longe, normais encolhem) | Aceite na v4; F1 resolve se for visível |
| `Content.mgcb` corrompido em falha ou edição concorrente | `MgcbEditMutex` + rollback como no `AssetImporter` |
| Materiais gerados apagados com Delete Project | Comportamento esperado (conteúdo do projeto); documentado |
| Licença do CHORD impede uso comercial | Opcional; `classic` e `sdxl_classic` cobrem o pipeline |
| Difusão produz iluminação "cozida" | Sufixo/negative do perfil + delighting + avisos de validação |
| Resultado "genérico" em vez de Source 2 | Perfil `source2` com intervalos e famílias; calibração D2 com checkpoint humano |
| Agente inventa APIs | Regra explícita; E1/E2 obrigam a ler o código local |
| Agente sem GPU | Backends `fake`/`classic`; tarefas [GPU] isoladas |
| Processo Python pendurado | Timeout + `Kill(entireProcessTree: true)` |
| Fugas de VRAM | Sem loader próprio na v4 (o `ContentManager` é dono); F1 exige dispose explícito |
