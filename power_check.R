N <- 108419
alpha <- 0.05/8
z <- qnorm(1 - alpha/2)
power_two_sided <- function(p0, p1, N, alpha) {
  h <- 2*(asin(sqrt(p1)) - asin(sqrt(p0)))
  z_a <- qnorm(1 - alpha/2)
  pnorm(-z_a - h*sqrt(N)) + (1 - pnorm(z_a - h*sqrt(N)))
}
cat("z_alpha/2 =", z, "\n")
cat("Power H1, against |p-0.5|=0.01     :", power_two_sided(0.5, 0.51, N, alpha), "\n")
cat("Power H2, against |p-0.4905|=0.01  :", power_two_sided(0.4905, 0.5005, N, alpha), "\n")
cat("Power H2, against |p-0.4905|=0.005 :", power_two_sided(0.4905, 0.4955, N, alpha), "\n")
cat("Power H2, against C_vs_C effect (|delta|=0.0025):", power_two_sided(0.4905, 0.488, N, alpha), "\n")
