# Progresso — Materiais PBR gerados por IA

Plano: [PLAN.md](PLAN.md). Ambiente: [ENVIRONMENT.md](ENVIRONMENT.md).

## Fase 0 — Inventário e decisões
- [x] 0.1 Inventário do ambiente
- [x] 0.2 Convenção do normal map — **DirectX / Y−** confirmado em `Scenes/NormalReliefTest_Top.obsc` (luz pontual rasante de cima: os tijolos DirectX ficam salientes, a cópia com o verde invertido fica afundada)
- [x] 0.3 Entradas mgcb para materiais gerados

## Fase A — Gerador `texgen` (Python)
- [x] A0 Esqueleto do pacote
- [x] A1 Backend `fake` e pipeline ponta a ponta
- [x] A2 Estimador PBR clássico
- [x] A3 Pós-processamento
- [x] A4 Perfil de estilo e validação
- [x] A5 Verificação de seams
- [x] A6 `texgen check` e `texgen styles`

## Fase B — Integração no motor (C#)
- [x] B1 `MaterialGenerationId`, `TexGenProtocol`, `TexGenSettings`
- [x] B2 `TexGenRunner`
- [x] B3 `GeneratedMaterialImporter`
- [x] B4 Bridge (`EnqueueImportGeneratedMaterial`)

## Fase C — Anvil e MCP
- [x] C1 Diálogo "Generate Material"
- [x] C2 Pré-visualização e variações
- [x] C3 Ferramenta MCP `materials_generate`
- [x] C4 Documentação

## Fase D — Validação visual
- [x] D1 Cena `MaterialLab.obsc` + `--material` / `--import-generated`
- [ ] D2 Calibração: ajustes aplicados (pipeline version 2, [D2_CALIBRATION.md](D2_CALIBRATION.md)); referência metálica medida (propostas por aplicar: height por contraste, piso de roughness dos metais); checkpoint visual humano feito

## Fase E — Backends GPU
- [x] E1 `sdxl_rgb.py`: verificado em GPU (RTX 3060, SDXL base 1.0 fp16): "red brick wall" seed 1 gerou, seam score 0.37, sem avisos, e foi visto no MaterialLab
- [x] E2 CHORD: licença aceite pelo utilizador (2026-10-21); `backends/chord_pbr.py` + backend `sdxl_chord` verificados em GPU (ver notas)

## Notas da execução (2026-10-21)

**0.2 — [HUMANO]**
- `Tools/TexGen/lab/generate_normal_convention_test.py` gera `Content/Scenes/NormalConventionTest.obsc` (sol vindo de −X) e `NormalConventionTest_B.obsc` (de +X), com `Bricks_Normal.jpg` (esquerda) e uma cópia com o verde invertido (`Bricks_Normal_FlipG.png`, registada no Content.mgcb).
- Capturas: `Engine.exe --scene Scenes/NormalConventionTest.obsc --shot out.png --frames 40` (e `_B`). Nas capturas feitas o sol quase não muda a imagem, por isso não dão resposta. Falta: abrir as cenas no Anvil (ou usar uma luz pontual rasante), ver qual das duas lajes tem as juntas escuras do lado oposto à luz nas duas variações e registar aqui.
- Enquanto não houver decisão, o `texgen` escreve DirectX / Y− (`--normal-convention`), como as texturas Poly Haven já em uso. Se for Y+, só muda o valor por defeito.

**0.3**
- `ContentManifest.RegisterGeneratedTextures` + `TexGenChecks` (4 verificações de manifesto, e com `--graphics` 4 de mgcb real: PNG cinzento 128 chega com R = 128, magenta fica opaco).
- A suite `--graphics` completa pára mais à frente em `PauseChecks` (já listado em `ISSUES.md`, não tem a ver com isto); as verificações novas correm antes e passam.

**Fase A**
- 48 testes `unittest` (`Tools\TexGen\.venv\Scripts\python -m unittest discover Tools/TexGen/tests`, a partir da raiz).
- Desvio ao plano na A5: a métrica de seams divide o gradiente da costura pelo percentil 95 (e pelo menos 3/4 do máximo) dos gradientes das outras colunas/linhas, em vez da média global, porque a média dava falsos positivos em tijolo/azulejo (juntas a começar na fronteira do tile). O limiar 1.5 mantém-se.
- Desvios menores: o `style` reforça uma variação mínima de roughness (desvio-padrão 0,03 × 1,5) em texturas quase planas; a saturação é limitada em HSV (0,75). `texgen.json` usa o nome da pasta de saída como `id` e `suggested.family` acrescenta a família resolvida.
- Os backends `sdxl_*` ficam por fazer (fase E); `check` explica porque não correm.
- Próximo passo: fase B (runner C# + importador), que depende de 0.3 e A6, ambos feitos.

**Fase B**
- Código em `Engine/Recources/TextureGeneration/`; verificações em `Tests/Components/TexGenChecks.Pipeline.cs` (correm no `dotnet run --project Tests\Components\Components.csproj`, sem `--graphics`).
- O runner é testado com o Python real do venv (gera, cache, backend indisponível, cancelamento); sem o venv o teste é saltado com `SKIP`.
- O importador é testado com um mgcb falso numa pasta Content temporária (`MaterialAsset.SourceRootOverride`); o mgcb real já está coberto pela 0.3. A fábrica `EditorBridge.GeneratedMaterialImporterFactory` (interna) permite ao teste do bridge fazer o mesmo.
- `assignToId` do bridge usa `MaterialAsset.Assign`; o teste não o cobre (precisa de um gameobject na cena), fica para a C1.
- Atenção: `MaterialGenerationId.PipelineVersion` e `PIPELINE_VERSION` do Python têm de andar juntos.
- Próximo passo: fase C (diálogo Generate Material no Anvil, pré-visualização, ferramenta MCP, documentação de utilizador).

**Fase C**
- Diálogo: `Editor/Anvil/ViewModels/GenerateMaterialViewModel.cs`, `Views/GenerateMaterialWindow.axaml(.cs)`; entradas no menu de contexto de Assets e em GameObject. O Import não fecha o diálogo (C2: duas seeds do mesmo prompt dão dois `.mat`).
- Verificado no Anvil com o driver (`run-obsidian`): abrir pelo menu Assets, escolher "test stone", Generate (backend `fake`) com as 5 miniaturas, Import (mgcb real, `.xnb` copiado, `.mat` aberto no Inspector) e o material num cubo. O material de teste foi apagado a seguir (Content, Content.mgcb e cópias de build).
- O driver não faz clique direito nem seleciona gameobjects de forma fiável; o menu de contexto foi aberto com um script de clique direito pontual (não está no repositório).
- O import bloqueia o motor durante o build do mgcb (cerca de 1–2 minutos nesta máquina com o Content atual); já está em `ISSUES.md`.
- MCP: `materials_generate` + `editor_get_operation` (jobs em `EditorAutomationService`). `assignToId` não tem teste automatizado (precisa de um gameobject na cena).
- Documentação: `Docs/markdown/Generated_Materials.md`, `Editor_MCP.md`.
- Próximos passos: 0.2 (decisão humana sobre o normal map), fase D (MaterialLab, calibração com referências CC0) e fase E (backends GPU).

**Fase D (2026-10-21)**
- `Tools/TexGen/lab/generate_material_lab.py [--reference-mats]` gera `Content/Scenes/MaterialLab.obsc` (chão de 16 lajes de 1 m, cubo, esfera, referências cinzenta 0.5 linear e cromo, sol, luz lateral, reflection probe) e `Materials/Reference/Poly_*.mat`. `LookDevCapture` ganhou `--material` (aplica a todos os `MaterialLab.Target*`) e `--import-generated <cache>` (importa pelo importador real e usa).
- **Descoberta**: o plano dizia que os modelos incorporados trazem tangentes; o `IsoSphere` não trazia (`GenerateTangentFrames=False`), e qualquer material com normal map nele fazia rebentar o renderer (vertex declaration sem tangentes). Corrigido em `Content.mgcb`. Ficam sem tangentes `sphere.x`, `dragon_uv_smooth.fbx` e as setas do editor (em `ISSUES.md`).
- D1 verificado: capturas de `Poly_Bricks`, `Poly_Cobblestone` e de um material gerado importado pelo mgcb real (13 s): sem NaN nem preto, textura repetida sem costuras visíveis, referências visíveis. O material de teste importado foi removido a seguir (Content.mgcb reposto, pasta e `.xnb` apagados).
- D2 (agente): ver [D2_CALIBRATION.md](D2_CALIBRATION.md). Os ajustes ao `source2.json` estão propostos, não aplicados. Falta a referência metálica e o checkpoint visual.

**Fase D, seguimento (2026-10-21)**
- Ajustes do `source2.json` aplicados (versão 2). Referência metálica Metal063 medida: o estimador deixava uma entrada quase plana cheia de relevo, por isso a height passou a seguir o contraste da entrada (`HEIGHT_REFERENCE_STD`) e o piso de roughness de `metal` desceu para 0.08 (versão 3). Inclinação do normal no Metal063: 0.54 -> 0.18 (referência 0.006); nas texturas de pedra/terra baixou também (Bricks 0.22, RockyDirt 0.38 contra 0.16/0.37). `PIPELINE_VERSION` e `MaterialGenerationId.PipelineVersion` = "3".

**Fase E (2026-10-21)**
- Máquina: RTX 3060 12 GB, mas o venv não tem `torch`/`diffusers` e `models/` não existe; a instalação de pacotes e os pesos são passos humanos (o código não acede à rede).
- `texgen/backends/sdxl_rgb.py`: `make_tileable` (todas as `Conv2d` da UNet e do VAE com `padding_mode="circular"`), `load_pipeline` (`StableDiffusionXLPipeline.from_pretrained(models/sdxl, float16, local_files_only=True)`, DPM++ 2M), `generate` (25 passos, gerador CUDA com a seed, progresso por passo, 1024 px e reduz se pedido menos). `check_backend` devolve ok para `sdxl_classic` quando tudo existe; `sdxl_chord` fica `backend_not_implemented` (E2).
- **As APIs de diffusers/torch usadas não foram executadas** (não estão instaladas): vêm do conhecimento do código, não de documentação instalada. A primeira execução real em GPU pode precisar de ajustes (nomes de argumentos do `callback_on_step_end`, etc.).
- Testes: `tests/test_sdxl.py` (ligação com a geração substituída: prompts do estilo e progresso; `make_tileable` com um modelo minúsculo em CPU, saltado sem torch).

**E1 verificado (2026-10-21)**
- Torch CUDA instalado, SDXL em `models/sdxl` (fp16; faltava o `model_index.json`, por isso `check` passou a exigi-lo). `python -m texgen generate --backend sdxl_classic --prompt "red brick wall" --seed 1` gerou a textura sem alterações ao código além do `variant="fp16"`.
- No MaterialLab (importação real) o relevo e a iluminação estão corretos e o chão repete sem costuras visíveis. Observação: o SDXL desenha um muro inteiro (muitas fiadas de tijolos pequenos) e o `suggested.tiling` da família brick (0.667) dá tijolos com ~5 cm; falta uma instrução de escala no prompt (por exemplo "close-up, 6 bricks across") ou outro `scale_m`.

**E2 CHORD (2026-10-21)**
- Código lido em `third_party/chord` (README, `test.py`, `chord/module/chord.py`, `stable_diffusion.py`). O modelo carrega as configs/tokenizer do SD 2.1 do Hub (`RedbeardNZ/stable-diffusion-2-1-base`, que já não existe); `hf_key` do `chord.yaml` aceita uma pasta local, por isso o backend usa `third_party/chord/sd21` (espelho público `sd2-community/stable-diffusion-2-1-base`, só configs e tokenizer) e não toca na rede. Os pesos vêm de `third_party/chord/weights/chord_v1.safetensors`.
- `texgen/backends/chord_pbr.py`: o checkpoint tem as chaves do transformers 4.x (`text_encoder.text_model.*`); com o 5.x do venv as chaves são traduzidas ao carregar (sem downgrade). A imagem é repetida 128 px à volta (1024 -> 1280), passa pelo CHORD em bfloat16 e é cortada ao centro. As saídas abertas são basecolor, normal, roughness e metalness (**sem height**: sai do albedo como no `classic`). O estilo `source2` aplica-se depois como no `classic`; o metalness do CHORD é binário mas o estilo decide-o pela família.
- Convenção do normal do CHORD: DirectX / Y-. Medido contra os mapas Poly Haven (correlação do verde +0.58 / +0.61 / +0.35 em Bricks / Cobblestone / RockyDirt) e convertido para Y+ internamente.
- Contra as mesmas referências a roughness acompanha melhor que o `classic` (Cobblestone: média 0.58 vs 0.58, correlação 0.81; o `classic` tinha ~0). A inclinação do normal é mais suave (0.08 / 0.12 / 0.05 contra 0.16 / 0.28 / 0.37 das referências).
- Tempo na RTX 3060: ~19 s a carregar, ~25 s por imagem a 1280 px. "red brick wall" seed 1: seam 0.34, sem avisos, relevo mais suave que o do `classic` no MaterialLab.
- Dependências instaladas no venv: torchvision 0.26.0+cu128, omegaconf, imageio. `texgen check --backend sdxl_chord` verifica pacotes, ficheiros do CHORD e CUDA.
- Os testes que esperavam "backend indisponível" (`test_cli.py`, `TexGenChecks.Pipeline.cs`, `TexGenMcpChecks.cs`) saltam onde o SDXL está instalado.
- 2026-10-21 (Claude): comparação com o look do Source 2 (HLA). Pipeline version 4: bandas de albedo / saturação por família, highlights com roll-off, prompts "aged, muted", fotos `--input-rgb` tornadas tileable, sombras de contacto suavizadas e declive das normais limitado no `classic`. Corrigido o parallax do G-buffer (usava SV_POSITION). Cena nova `Scenes/Source2MaterialTest.obsc` + `lab/compare_source2_look.py`; `LookDevCapture --tiling`. Detalhes em [D2_CALIBRATION.md](D2_CALIBRATION.md) > Source 2 look check.
