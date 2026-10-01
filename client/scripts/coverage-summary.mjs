// Prints the Vitest coverage totals (coverage/coverage-summary.json) as a Markdown table for
// $GITHUB_STEP_SUMMARY. Usage: node scripts/coverage-summary.mjs >> "$GITHUB_STEP_SUMMARY"
import { readFileSync } from "node:fs";

const { total } = JSON.parse(readFileSync(new URL("../coverage/coverage-summary.json", import.meta.url), "utf8"));
const metrics = ["lines", "branches", "functions", "statements"];

console.log("### Client coverage\n");
console.log(`| ${metrics.map((m) => m[0].toUpperCase() + m.slice(1)).join(" | ")} |`);
console.log(`|${metrics.map(() => "---").join("|")}|`);
console.log(`| ${metrics.map((m) => `${total[m].pct}%`).join(" | ")} |`);
