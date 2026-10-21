# Progresso — Materiais PBR gerados por IA

Plano: [PLAN.md](PLAN.md). Ambiente: [ENVIRONMENT.md](ENVIRONMENT.md).

## Fase 0 — Inventário e decisões
- [x] 0.1 Inventário do ambiente
- [ ] 0.2 Convenção do normal map — cenas e capturas feitas, mas **inconclusivas** (o sol quase não se nota); **aguarda decisão humana** (ver abaixo)
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

D, E: por fazer.

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
