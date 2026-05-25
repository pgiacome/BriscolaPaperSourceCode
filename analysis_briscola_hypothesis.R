# =============================================================================
# Statistical analysis of a Monte Carlo Briscola tournament
# =============================================================================
# Companion to Program.cs (BriscolaGreedy .NET 9 project). Reads the CSV produced
# by the simulator and performs the hypothesis tests reported in the paper.
#
# Hypotheses (pre-registered):
#   H1 (folklore): the player with the majority of briscola cards wins more
#                  often than expected by chance.
#   H2 (paper):    at least one of the non-greedy strategies (Hoarder, Counter)
#                  achieves a higher head-to-head win rate against Greedy than
#                  Greedy achieves against itself, independently of the briscola
#                  count.
#
# Usage from an R console:
#   Rscript analysis_briscola_hypothesis.R [path/to/briscola_simulazione.csv]
#
# Output: analisi_briscola.log (stdout+messages) and briscola_analysis_extended.csv.
# =============================================================================

suppressPackageStartupMessages({
  library(tidyverse)
  library(binom)
  library(psych)
  library(broom)
})

args <- commandArgs(trailingOnly = TRUE)
csv_path <- if (length(args) >= 1) args[1] else "briscola_simulazione.csv"
if (!file.exists(csv_path)) {
  stop(sprintf("Simulation CSV not found: %s", csv_path))
}

log_file <- file("analisi_briscola.log", open = "wt")
sink(log_file)
sink(log_file, type = "message")

cat("=== INPUT ===\n")
cat("CSV:", csv_path, "\n\n")

# -----------------------------------------------------------------------------
# Load data
# -----------------------------------------------------------------------------
data <- read.csv(csv_path, sep = ";", stringsAsFactors = FALSE)
names(data) <- trimws(tolower(names(data)))

cat("=== HEAD ===\n"); print(head(data))
cat("\n=== STR ===\n");  str(data)

# One row per game (last trick of each game carries the final totals)
games <- data %>%
  group_by(partitaid) %>%
  slice(n()) %>%
  ungroup() %>%
  mutate(
    strategy_pair = paste0(strategyg1, "_vs_", strategyg2),
    piubriscole   = case_when(
      briscoletotalig1 > briscoletotalig2 ~ "G1",
      briscoletotalig2 > briscoletotalig1 ~ "G2",
      TRUE ~ "Tie"
    ),
    winner                      = vincitorepartita,
    briscola_hypothesis_correct = (piubriscole == winner),
    briscoladiff                = briscoletotalig1 - briscoletotalig2,
    puntidiff                   = puntifinalig1 - puntifinalig2
  )

cat("\n=== GAMES PER STRATEGY PAIR ===\n")
print(games %>% count(strategy_pair))

# -----------------------------------------------------------------------------
# H1: briscola majority predicts winning (aggregated, then per matchup)
# -----------------------------------------------------------------------------
cat("\n\n=== H1. BRISCOLA MAJORITY vs WINNER ===\n")

games_no_tie <- games %>% filter(piubriscole != "Tie", winner != "Pareggio")

contingency <- table(games_no_tie$piubriscole, games_no_tie$winner)
cat("Contingency table (briscola majority x winner):\n"); print(contingency)

chi <- chisq.test(contingency)
print(chi)

successes <- sum(games_no_tie$briscola_hypothesis_correct, na.rm = TRUE)
total     <- nrow(games_no_tie)
prop      <- successes / total
wilson    <- binom.confint(successes, total, methods = "wilson")
cat("\nProportion where 'more briscola == winner':\n")
print(data.frame(successes = successes, total = total, proportion = prop,
                 wilson_lower = wilson$lower, wilson_upper = wilson$upper))

# Per-matchup (the interesting part: does the association survive for each pair?)
cat("\nPer-matchup association:\n")
per_matchup <- games_no_tie %>%
  group_by(strategy_pair) %>%
  summarise(
    n      = n(),
    prop   = mean(briscola_hypothesis_correct),
    lower  = binom.confint(sum(briscola_hypothesis_correct), n(), methods = "wilson")$lower,
    upper  = binom.confint(sum(briscola_hypothesis_correct), n(), methods = "wilson")$upper,
    .groups = "drop"
  )
print(per_matchup)

# -----------------------------------------------------------------------------
# H2: strategy dominance over greedy baseline
# -----------------------------------------------------------------------------
cat("\n\n=== H2. STRATEGY DOMINANCE (HEAD-TO-HEAD) ===\n")

win_rates <- games %>%
  filter(winner != "Pareggio") %>%
  group_by(strategy_pair, strategyg1, strategyg2) %>%
  summarise(
    n_games      = n(),
    wins_g1      = sum(winner == "G1"),
    win_rate_g1  = wins_g1 / n_games,
    lower        = binom.confint(wins_g1, n_games, methods = "wilson")$lower,
    upper        = binom.confint(wins_g1, n_games, methods = "wilson")$upper,
    .groups      = "drop"
  )
print(win_rates, n = nrow(win_rates))

# Pairwise comparison: (S vs Greedy) vs (Greedy vs Greedy) as G1
baseline_g1 <- win_rates %>% filter(strategy_pair == "Greedy_vs_Greedy") %>% pull(win_rate_g1)
cat(sprintf("\nGreedy-vs-Greedy baseline win rate for G1: %.4f\n", baseline_g1))

# Bonferroni-corrected binomial tests
tests <- win_rates %>%
  filter(strategy_pair != "Greedy_vs_Greedy") %>%
  rowwise() %>%
  mutate(
    p_value = binom.test(wins_g1, n_games, p = baseline_g1, alternative = "two.sided")$p.value
  ) %>%
  ungroup() %>%
  mutate(p_bonferroni = pmin(1, p_value * n()))
cat("\nPairwise vs baseline (Bonferroni-corrected):\n")
print(tests %>% select(strategy_pair, n_games, win_rate_g1, lower, upper, p_value, p_bonferroni))

# -----------------------------------------------------------------------------
# H3 (exploratory): logistic regression
# -----------------------------------------------------------------------------
cat("\n\n=== H3. LOGISTIC REGRESSION ===\n")

glm_data <- games %>%
  filter(winner != "Pareggio") %>%
  mutate(
    g1_wins    = as.integer(winner == "G1"),
    strat_g1   = factor(strategyg1, levels = c("Greedy", "Hoarder", "Counter")),
    strat_g2   = factor(strategyg2, levels = c("Greedy", "Hoarder", "Counter"))
  )

fit <- glm(g1_wins ~ strat_g1 + strat_g2 + briscoladiff,
           data = glm_data, family = binomial())
cat("Logistic regression coefficients (log-odds):\n")
print(tidy(fit, conf.int = TRUE, conf.level = 0.95))
cat("\nOdds ratios:\n")
print(tidy(fit, exponentiate = TRUE, conf.int = TRUE, conf.level = 0.95))

# -----------------------------------------------------------------------------
# Correlation: briscola advantage vs point margin (per matchup)
# -----------------------------------------------------------------------------
cat("\n\n=== CORRELATION: briscola diff vs points diff ===\n")
cors <- games %>%
  group_by(strategy_pair) %>%
  summarise(
    n           = n(),
    pearson     = cor(briscoladiff, puntidiff, method = "pearson"),
    spearman    = cor(briscoladiff, puntidiff, method = "spearman"),
    .groups     = "drop"
  )
print(cors)

# -----------------------------------------------------------------------------
# Break-even analysis (how large must the briscola advantage be to matter?)
# -----------------------------------------------------------------------------
cat("\n\n=== BREAK-EVEN BY BRISCOLA DIFFERENCE ===\n")
break_even <- games %>%
  filter(winner != "Pareggio") %>%
  group_by(strategy_pair, briscoladiff) %>%
  summarise(
    games       = n(),
    wins_g1     = sum(winner == "G1"),
    win_rate_g1 = wins_g1 / games,
    .groups = "drop"
  ) %>%
  arrange(strategy_pair, briscoladiff)
print(break_even, n = nrow(break_even))

# -----------------------------------------------------------------------------
# Export extended game-level dataset for downstream plots
# -----------------------------------------------------------------------------
write.csv(games, "briscola_analysis_extended.csv", row.names = FALSE)
cat("\nExtended data written to: briscola_analysis_extended.csv\n")

sink(type = "message")
sink()
