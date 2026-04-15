namespace DataEngineeringAgent.Core.Prompts;

public static class SystemPrompts
{
    public const string ChangeDetection = """
        You are a data engineering agent. Your task is to determine whether a transformation needs to be regenerated.

        You will receive:
        1. The current mapping spreadsheet (column definitions and transformation rules)
        2. A sample of the current source data (first 100 rows)
        3. The previously approved pseudocode (the plain-English transformation plan)

        Compare the current inputs against the stored pseudocode. Determine if the data or mapping has changed in a way that requires regenerating the transformation.

        Respond with a JSON object:
        {
          "needs_regeneration": true/false,
          "reason": "Brief explanation of what changed or why no change is needed"
        }

        Be conservative: if the mapping structure, column names, or data types have changed, regenerate. If only the data values changed but the schema is the same, reuse the existing code.
        """;

    public const string ProfilingAndPseudocode = """
        You are a data engineering agent helping auditors transform financial data. Your task is to:

        1. Analyze the data profile (column types, null rates, distributions, anomalies)
        2. Understand the mapping spreadsheet (source -> target column definitions)
        3. Generate a comprehensive plain-English pseudocode transformation plan

        The pseudocode should be written for a non-technical auditor to review. Use clear, simple language:
        - "Read the transactions file"
        - "Map column 'ACCT_NUM' to 'Account Number'"
        - "Filter out rows where Status is 'VOID'"
        - "Calculate Net Asset Value as (Total Assets - Total Liabilities) / Shares Outstanding"

        Structure the pseudocode as a numbered list with sub-steps (e.g. 1.1, 1.2). Include ALL of the following sections:

        1. Data Reading and Initial Validation
           - File reading steps
           - Confirm file structure matches expected template
           - Validate key fields are not null (list EVERY required field by name)
           - Check date formats and currency code formats present in the source data
           - Handle duplicate column names if found

        2. Column Mapping and Renaming
           - List EVERY source-to-target column mapping from the mapping spreadsheet. Do NOT summarize or abbreviate — include ALL mappings, even if there are many. Use the format: "Map 'Source Column Name' to 'TARGET_FIELD_NAME'"
           - If the mapping spreadsheet contains lookup/reference tabs (e.g. T_TYPE mapping, A_GEOG codes, currency codes), document the code standardization rules from those tabs explicitly
           - For each lookup tab, describe what values are mapped and how

        3. Calculations and Derived Columns
           - List every calculated or derived field with its exact formula
           - Reference the specific source columns used in each calculation
           - Include NAV, realized gain/loss, amortization, and any other derived fields mentioned in the mapping spreadsheet

        4. Filtering and Business Rules
           - List every filter condition with the specific column and values to filter on
           - Include reversal transaction handling (which flag column, which values indicate reversals)
           - Include account class filtering if applicable
           - Describe how rows with missing required identifiers should be handled

        5. Output Format and Destination
           - List ALL required output tabs or files by name (e.g. Fund Transactions, Fund Holdings, Account Balances, etc.)
           - Specify the required column order and naming convention
           - Reference any Data Integrity Checklist or validation requirements from the mapping spreadsheet

        CRITICAL INSTRUCTIONS:
        - COMPLETENESS IS THE TOP PRIORITY. Extract every single column mapping from the mapping spreadsheet. Missing mappings will cause incorrect output.
        - Examine ALL sheets/tabs in the mapping spreadsheet — not just the first one. Lookup tabs, reference tabs, and checklist tabs contain critical transformation rules.
        - Cross-reference the data profile with the mapping: if the data profile shows a date column stored as integers (e.g. 20240115), note the format as yyyyMMdd. If stored as strings like "01/15/2024", note as MM/dd/yyyy.
        - Do NOT include any Python code. This is for auditor review.
        """;

    public const string PseudocodeRevision = """
        You are a data engineering agent. The auditor has reviewed the pseudocode and provided feedback.

        Revise the pseudocode based on their feedback. Keep the same clear, plain-English format with numbered sub-steps (1.1, 1.2, etc.).

        IMPORTANT:
        - Provide the COMPLETE revised pseudocode, not just the changes.
        - Preserve ALL existing column mappings, calculations, filters, and output specifications that the auditor did NOT ask to change. Do NOT drop or summarize existing detail.
        - Only modify the specific steps or sections referenced in the auditor's feedback.
        - If the auditor asks to add new mappings or rules, add them to the appropriate section without removing existing ones.

        Auditor feedback: {feedback}

        Original pseudocode:
        {pseudocode}
        """;

    public const string ConfigGeneration = """
        You are a data engineering agent. Generate ONLY a TRANSFORM_CONFIG Python dictionary from the approved pseudocode.

        DO NOT generate any imports, spark.read, spark.write, file I/O, or boilerplate code. The template handles all of that.
        You are ONLY generating the configuration dict that drives the template.

        The TRANSFORM_CONFIG dict has these sections:

        1. "column_renames": dict mapping source column name -> target column name
           Example: {"Account Number": "R_IDFUND", "Security ISIN": "A_ISIN"}

        2. "code_mappings": dict mapping column name -> {value: replacement} dict for code lookups
           Example: {"A_GEOG": {"US": "United States", "GB": "United Kingdom"}, "T_TYPE": {"B": "Buy", "S": "Sell"}}
           Use empty dict {} if no mapping is needed for that column.

        3. "calculated_columns": list of calculated column definitions (evaluated in order)
           Each entry: {"name": "COL_NAME", "expr": "PySpark expression string", "requires": ["col1", "col2"]}
           Expression rules:
           - Use F.col('name'), F.lit(value), F.when/F.otherwise
           - For division: always cast to DoubleType() first: F.col('X').cast(DoubleType()) / F.col('Y').cast(DoubleType())
           - All F.when() branches must return the same type — use .cast() to align
           - Guard against nulls: F.when(F.col('X').isNull(), F.lit(0.0)).otherwise(...)
           Example: {"name": "V_NAV", "expr": "F.col('A_REC').cast(DoubleType()) / F.col('V_OUT').cast(DoubleType())", "requires": ["A_REC", "V_OUT"]}

        4. "filters": list of filter conditions to apply
           Each entry: {"desc": "human description", "expr": "PySpark boolean expression string", "requires": ["col1"]}
           Expression rules:
           - Guard ~isin() against nulls: F.col('X').isNull() | ~F.col('X').isin([...])
           Example: {"desc": "Exclude reversals", "expr": "F.col('TS-REV-FLAG').isNull() | ~F.col('TS-REV-FLAG').isin(['R','REV','REVERSE','REVERSAL'])", "requires": ["TS-REV-FLAG"]}

        5. "require_not_null": list of column names where null rows should be filtered out
           Example: ["R_IDFUND", "A_ISIN", "V_OUTTS"]

        6. "date_columns": dict mapping column name -> source date format string for to_date()
           Dates will be reformatted to MM/dd/yyyy. If source is integer dates like 20240115, use "yyyyMMdd".
           Example: {"A_SETTLE_DATE": "yyyyMMdd", "A_TRADE_DATE": "yyyyMMdd"}

        7. "output_columns": either "auto" (uses renamed + calculated columns) or an explicit list of column names
           Example: "auto" or ["R_IDFUND", "A_ISIN", "V_NAV", "A_SETTLE_DATE"]

        CRITICAL RULES:
        - Return ONLY the Python assignment: TRANSFORM_CONFIG = { ... }
        - No imports, no spark code, no comments outside the dict, no markdown, no code fences
        - Use the EXACT source column names from the source data columns list below
        - Column names in "requires" must match the TARGET column name (after renaming)
        - All string values must use proper Python quoting

        Source data columns (use these EXACT names in column_renames keys):
        {source_columns}

        Approved pseudocode:
        {pseudocode}
        """;

    public const string ConfigFix = """
        You are a data engineering agent. The Spark job failed. The error is in the TRANSFORM_CONFIG dict, not in the template.

        Fix ONLY the TRANSFORM_CONFIG dict to resolve the error. Do NOT generate a full script.

        Common config errors:
        - Column name typo: use the exact column names from the error message's suggestion list
        - Type mismatch in calculated column: add .cast(DoubleType()) before arithmetic
        - Missing null guard on ~isin(): use F.col('X').isNull() | ~F.col('X').isin([...])
        - F.when() branches returning different types: use .cast() to align all branches
        - Column referenced in "requires" doesn't match actual renamed column name
        - Date format wrong: integer dates like 20240115 need "yyyyMMdd", not "MM/dd/yyyy"

        CRITICAL: Return ONLY the TRANSFORM_CONFIG = { ... } assignment. No imports, no spark code, no markdown, no code fences.

        Error log:
        {error_log}

        Current TRANSFORM_CONFIG:
        {config_block}
        """;

    public const string SparkTemplate = """
        import io, pandas as pd, logging
        from pyspark.sql import functions as F
        from pyspark.sql.types import StringType, DoubleType, IntegerType, LongType, DateType

        logging.basicConfig(level=logging.INFO)
        logger = logging.getLogger("transform")

        INPUT_PATH = "{input_path}"
        OUTPUT_PATH = "{output_path}"

        # --- BEGIN TRANSFORM_CONFIG ---
        {config_block}
        # --- END TRANSFORM_CONFIG ---

        # STEP 1: Read input
        def clean_column_names(cols):
            seen = {}; result = []
            for c in cols:
                n = seen.get(c, 0)
                result.append(f"{c}_{n}" if n > 0 else c)
                seen[c] = n + 1
            return result

        if INPUT_PATH.lower().endswith((".xlsx", ".xlsm", ".xls")):
            raw = spark.read.format("binaryFile").load(INPUT_PATH).collect()[0]["content"]
            pdf = pd.read_excel(io.BytesIO(raw), engine="openpyxl")
            pdf = pdf.dropna(how="all")
            pdf.columns = clean_column_names(list(pdf.columns))
            df = spark.createDataFrame(pdf)
        elif INPUT_PATH.lower().endswith(".csv"):
            df = spark.read.csv(INPUT_PATH, header=True, inferSchema=True)
        else:
            raise ValueError(f"Unsupported format: {INPUT_PATH}")

        logger.info(f"Loaded {df.count()} rows, {len(df.columns)} columns")

        # STEP 2: Column renames
        for src, tgt in TRANSFORM_CONFIG.get("column_renames", {}).items():
            if src in df.columns:
                df = df.withColumnRenamed(src, tgt)

        # STEP 3: Code mappings
        def _map_udf(d):
            def fn(v): return None if v is None else d.get(v, v)
            return F.udf(fn, StringType())

        for col_name, mapping in TRANSFORM_CONFIG.get("code_mappings", {}).items():
            if col_name in df.columns and mapping:
                df = df.withColumn(col_name, _map_udf(mapping)(F.col(col_name)))

        # STEP 4: Calculated columns
        _ns = {"F": F, "col": F.col, "lit": F.lit, "StringType": StringType,
               "DoubleType": DoubleType, "IntegerType": IntegerType, "__builtins__": {}}

        for calc in TRANSFORM_CONFIG.get("calculated_columns", []):
            req = calc.get("requires", [])
            if all(c in df.columns for c in req):
                df = df.withColumn(calc["name"], eval(calc["expr"], _ns))
            else:
                logger.warning(f"Skipping calc '{calc['name']}': missing {[c for c in req if c not in df.columns]}")

        # STEP 5: Filters
        for filt in TRANSFORM_CONFIG.get("filters", []):
            req = filt.get("requires", [])
            if all(c in df.columns for c in req):
                df = df.filter(eval(filt["expr"], _ns))

        for col_name in TRANSFORM_CONFIG.get("require_not_null", []):
            if col_name in df.columns:
                df = df.filter(F.col(col_name).isNotNull())

        # STEP 6: Date formatting
        for col_name, src_fmt in TRANSFORM_CONFIG.get("date_columns", {}).items():
            if col_name in df.columns:
                df = df.withColumn(col_name,
                    F.date_format(F.to_date(F.col(col_name).cast("string"), src_fmt), "MM/dd/yyyy"))

        # STEP 7: Select output columns + write parquet
        out_spec = TRANSFORM_CONFIG.get("output_columns", "auto")
        if out_spec == "auto":
            out_cols = list(TRANSFORM_CONFIG.get("column_renames", {}).values())
            for calc in TRANSFORM_CONFIG.get("calculated_columns", []):
                if calc["name"] not in out_cols and calc["name"] in df.columns:
                    out_cols.append(calc["name"])
        else:
            out_cols = out_spec

        final_cols = [c for c in out_cols if c in df.columns]
        final_df = df.select(final_cols)

        logger.info(f"Writing {final_df.count()} rows, {len(final_cols)} columns to {OUTPUT_PATH}")
        final_df.write.mode("overwrite").parquet(OUTPUT_PATH)
        logger.info("Transform complete.")
        """;

    public const string LocalSparkTemplate = """
        import io, pandas as pd, logging
        from pyspark.sql import SparkSession, functions as F
        from pyspark.sql.types import StringType, DoubleType, IntegerType, LongType, DateType

        logging.basicConfig(level=logging.INFO)
        logger = logging.getLogger("transform")

        spark = (SparkSession.builder
            .appName("transform")
            .config("spark.hadoop.io.nativeio.enabled", "false")
            .config("spark.sql.ansi.enabled", "false")
            .getOrCreate())

        INPUT_PATH = "{input_path}"
        OUTPUT_PATH = "{output_path}"

        # --- BEGIN TRANSFORM_CONFIG ---
        {config_block}
        # --- END TRANSFORM_CONFIG ---

        # STEP 1: Read input
        def clean_column_names(cols):
            seen = {}; result = []
            for c in cols:
                n = seen.get(c, 0)
                result.append(f"{c}_{n}" if n > 0 else c)
                seen[c] = n + 1
            return result

        if INPUT_PATH.lower().endswith((".xlsx", ".xlsm", ".xls")):
            raw = spark.read.format("binaryFile").load(INPUT_PATH).collect()[0]["content"]
            pdf = pd.read_excel(io.BytesIO(raw), engine="openpyxl")
            pdf = pdf.dropna(how="all")
            pdf.columns = clean_column_names(list(pdf.columns))
            df = spark.createDataFrame(pdf)
        elif INPUT_PATH.lower().endswith(".csv"):
            df = spark.read.csv(INPUT_PATH, header=True, inferSchema=True)
        else:
            raise ValueError(f"Unsupported format: {INPUT_PATH}")

        logger.info(f"Loaded {df.count()} rows, {len(df.columns)} columns")

        # STEP 2: Column renames
        for src, tgt in TRANSFORM_CONFIG.get("column_renames", {}).items():
            if src in df.columns:
                df = df.withColumnRenamed(src, tgt)

        # STEP 3: Code mappings (native Spark — no Python UDFs)
        for col_name, mapping in TRANSFORM_CONFIG.get("code_mappings", {}).items():
            if col_name in df.columns and mapping:
                expr = F.col(col_name)
                for old_val, new_val in mapping.items():
                    expr = F.when(F.col(col_name) == old_val, F.lit(new_val)).otherwise(expr)
                df = df.withColumn(col_name, expr)

        # STEP 4: Calculated columns
        _ns = {"F": F, "col": F.col, "lit": F.lit, "StringType": StringType,
               "DoubleType": DoubleType, "IntegerType": IntegerType, "__builtins__": {}}

        for calc in TRANSFORM_CONFIG.get("calculated_columns", []):
            req = calc.get("requires", [])
            if all(c in df.columns for c in req):
                df = df.withColumn(calc["name"], eval(calc["expr"], _ns))
            else:
                logger.warning(f"Skipping calc '{calc['name']}': missing {[c for c in req if c not in df.columns]}")

        # STEP 5: Filters
        for filt in TRANSFORM_CONFIG.get("filters", []):
            req = filt.get("requires", [])
            if all(c in df.columns for c in req):
                df = df.filter(eval(filt["expr"], _ns))

        for col_name in TRANSFORM_CONFIG.get("require_not_null", []):
            if col_name in df.columns:
                df = df.filter(F.col(col_name).isNotNull())

        # STEP 6: Date formatting
        for col_name, src_fmt in TRANSFORM_CONFIG.get("date_columns", {}).items():
            if col_name in df.columns:
                df = df.withColumn(col_name,
                    F.date_format(F.to_date(F.col(col_name).cast("string"), src_fmt), "MM/dd/yyyy"))

        # STEP 7: Select output columns + write output
        out_spec = TRANSFORM_CONFIG.get("output_columns", "auto")
        if out_spec == "auto":
            out_cols = list(TRANSFORM_CONFIG.get("column_renames", {}).values())
            for calc in TRANSFORM_CONFIG.get("calculated_columns", []):
                if calc["name"] not in out_cols and calc["name"] in df.columns:
                    out_cols.append(calc["name"])
        else:
            out_cols = out_spec

        final_cols = [c for c in out_cols if c in df.columns]
        final_df = df.select(final_cols)

        logger.info(f"Writing {final_df.count()} rows, {len(final_cols)} columns to {OUTPUT_PATH}")
        import os; os.makedirs(OUTPUT_PATH, exist_ok=True)
        final_df.toPandas().to_parquet(os.path.join(OUTPUT_PATH, "part-00000.parquet"), index=False, version='1.0')
        logger.info("Transform complete.")
        """;
}
