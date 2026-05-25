# BriscolaPaperSourceCode

Source code accompanying the paper *Greedy dominance in two-player Briscola: a
Monte Carlo study* by Piero Giacomelli.

The repository contains a deterministic .NET 8 Monte Carlo simulator and three R
scripts that reproduce every numerical result, table and figure reported in the
paper.

## Contents

| File                              | Role                                                                                          |
| --------------------------------- | --------------------------------------------------------------------------------------------- |
| `Program.cs`                      | Round-robin Briscola simulator: Greedy, Hoarder and Counter policies, deterministic in seed.  |
| `BriscolaGreedy.csproj`           | .NET 8 project file.                                                                          |
| `BriscolaGreedy.sln`              | Visual Studio solution.                                                                       |
| `analysis_briscola_hypothesis.R`  | Hypothesis tests on the per-trick CSV; writes `analisi_briscola.log` and the game-level CSV.  |
| `make_figures.R`                  | Builds `fig_breakeven.pdf`, `tab_winrates.tex`, `tab_logit.tex`, `tab_briscola_use.tex`.      |
| `power_check.R`                   | A-priori power calculation for the briscola-majority binomial test.                           |

## Reproducibility pipeline

The full pipeline is deterministic in the RNG seed and produces every number,
table and figure in the paper.

```text
1. dotnet run -c Release --project BriscolaGreedy.csproj -- \
       --seed 42 --total-games 1000000 --output briscola_simulazione.csv
   # ~2.7 GB per-trick CSV

2. Rscript analysis_briscola_hypothesis.R briscola_simulazione.csv
   # writes analisi_briscola.log and briscola_analysis_extended.csv

3. Rscript make_figures.R
   # reads both of the above and writes fig_breakeven.pdf,
   # tab_winrates.tex, tab_logit.tex, tab_briscola_use.tex
```

## Prerequisites

- .NET 8 SDK (the simulator uses `net8.0`)
- R >= 4.2 with the packages used by the scripts (`tidyverse`, `broom`, `binom`,
  `xtable`, `ggplot2`)

## License

Released under the MIT License. See the paper's Reproducibility section.
