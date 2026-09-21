# Eval probe-panel — v1.1 (2026-09-22: absent-item check names the item; harness h2)

prompts: `worktree` · model `gpt-5.4-mini` · max_tokens 200 · 1 scenarios / 2 runs · 7 s
pass 0 · flaky 1 · fail 0 · finish_reason=length in 0 runs · tokens 0+0 ≈ $0.0 (at $0.15/$0.6 per 1M — assumption)

| scenario | vertical | mode | pass rate | failed checks |
|---|---|---|---|---|
| pn_installment_kaspi | kaspi_seller | panel | 0.50 (2×) | cards_no_invented_installment_terms |

## By check

| check | pass/total |
|---|---|
| cards | 2/2 |
| cards_no_invented_installment_terms | 1/2 |
