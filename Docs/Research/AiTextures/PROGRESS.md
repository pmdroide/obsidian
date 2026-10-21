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

## Fases seguintes
B, C, D, E: por fazer.

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
