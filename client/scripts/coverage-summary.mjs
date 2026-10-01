// Prints the Vitest coverage totals (coverage/coverage-summary.json) as a Markdown table for
// $GITHUB_STEP_SUMMARY. Usage: node scripts/coverage-summary.mjs >> "$GITHUB_STEP_SUMMARY"
// When an earlier step failed before the tests ran there is no report; that is not an error here.
import { existsSync, readFileSync } from "node:fs";

const reportUrl = new URL("../coverage/coverage-summary.json", import.meta.url);

if (!existsSync(reportUrl)) {
  console.log("### Client coverage\n\nNo coverage report: the tests did not run.");
  process.exit(0);
}

const { total } = JSON.parse(readFileSync(reportUrl, "utf8"));
const metrics = ["lines", "branches", "functions", "statements"];

console.log("### Client coverage\n");
console.log(`| ${metrics.map((m) => m[0].toUpperCase() + m.slice(1)).join(" | ")} |`);
console.log(`|${metrics.map(() => "---").join("|")}|`);
console.log(`| ${metrics.map((m) => `${total[m].pct}%`).join(" | ")} |`);
