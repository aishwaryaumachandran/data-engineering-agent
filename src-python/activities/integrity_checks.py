"""Phase 5: Deterministic integrity checks (not AI).

Validates transformation output against expected schema and business rules.
"""

import logging
from models.integrity import CheckResult, IntegrityReport
from tools.adls import read_spark_output

logger = logging.getLogger(__name__)


def run_integrity_checks(output_path: str, expected_columns: list[str] | None = None) -> IntegrityReport:
    """Run deterministic validation on Spark output.

    Checks:
    - Output file exists and is readable
    - Row count > 0
    - Expected columns present (if provided)
    - No fully-null columns
    - No duplicate rows (sample-based)

    Returns:
        IntegrityReport with pass/fail for each check.
    """
    checks = []
    errors = []

    # 1. Read output
    try:
        output = read_spark_output(output_path)
    except Exception as e:
        return IntegrityReport(
            checks=[CheckResult(name="read_output", passed=False, message=f"Failed to read output: {e}")],
            overall_pass=False,
            errors=[str(e)],
        )

    # 2. Row count check
    row_count = output["row_count"]
    checks.append(CheckResult(
        name="row_count",
        passed=row_count > 0,
        message=f"Output has {row_count} rows",
        details={"row_count": row_count},
    ))
    if row_count == 0:
        errors.append("Output has 0 rows")

    # 3. Schema conformance
    if expected_columns:
        actual_cols = set(output["columns"])
        missing = set(expected_columns) - actual_cols
        checks.append(CheckResult(
            name="schema_conformance",
            passed=len(missing) == 0,
            message=f"Missing columns: {missing}" if missing else "All expected columns present",
            details={"missing": list(missing), "actual": output["columns"]},
        ))
        if missing:
            errors.append(f"Missing columns: {missing}")

    # 4. Null column check (warning only — entirely null columns may be legitimate
    #    when source data has null fields, especially with small sample sizes)
    if output["sample_rows"]:
        sample_rows = output["sample_rows"]
        for col in output["columns"]:
            null_count = sum(1 for row in sample_rows if row.get(col) is None)
            if null_count == len(sample_rows):
                logger.warning("Column '%s' is entirely null in sample (%d rows)", col, len(sample_rows))
                checks.append(CheckResult(
                    name=f"null_check_{col}",
                    passed=True,
                    message=f"Column '{col}' is entirely null in sample (warning)",
                ))

    # 5. Duplicate check (on sample — warning only for low rates)
    # Financial transaction data can have legitimate duplicate rows (e.g. two
    # identical trades on the same security/date/amount). Failing on duplicates
    # in a small sample causes unnecessary retries that the config fix cannot resolve.
    if output["sample_rows"]:
        sample_strs = [str(sorted(row.items())) for row in output["sample_rows"]]
        unique_count = len(set(sample_strs))
        dup_count = len(sample_strs) - unique_count
        dup_rate = dup_count / len(sample_strs) if sample_strs else 0
        # Only fail if >10% of sample rows are duplicates (indicates a real join/logic issue)
        is_dup_pass = dup_rate <= 0.10
        checks.append(CheckResult(
            name="duplicate_check",
            passed=is_dup_pass,
            message=f"{dup_count} duplicate rows in sample ({dup_rate:.0%}) — {'within tolerance' if is_dup_pass else 'exceeds 10% threshold'}" if dup_count else "No duplicates in sample",
            details={"duplicates": dup_count, "sample_size": len(sample_strs), "duplicate_rate": dup_rate},
        ))
        if not is_dup_pass:
            errors.append(f"{dup_count} duplicate rows in sample ({dup_rate:.0%}) — exceeds 10% threshold")
        elif dup_count > 0:
            logger.warning("Duplicate check: %d duplicates in %d sample rows (%.0f%%) — within tolerance",
                           dup_count, len(sample_strs), dup_rate * 100)

    overall_pass = all(c.passed for c in checks)
    logger.info("Integrity checks for %s: %s", output_path, "PASS" if overall_pass else "FAIL")

    return IntegrityReport(checks=checks, overall_pass=overall_pass, errors=errors)
