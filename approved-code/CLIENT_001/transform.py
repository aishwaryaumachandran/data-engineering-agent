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

INPUT_PATH = "C:/Users/umachandrana/Documents/Delloitte/DNAV/Code_New/data-engineering-agent/input_data/CLIENT_001/client/transactions.xlsx"
OUTPUT_PATH = "C:/Users/umachandrana/Documents/Delloitte/DNAV/Code_New/data-engineering-agent/output/CLIENT_001/20260415_164126"

# --- BEGIN TRANSFORM_CONFIG ---
TRANSFORM_CONFIG = {
    "column_renames": {
        "Cost-Local-Transaction": "A_BOOKVALUE_AC",
        "Cost-Basis-Transaction": "A_BOOKVALUE_FC",
        "Currency Code-Trade": "A_CURR",
        "Security Number (CUSIP/CINS)": "A_CUSIP",
        "Security Number Full": "A_IDINT",
        "Security ISIN": "A_ISIN",
        "Long or Short Position": "A_LONGSHORT",
        "Bond Maturity Date": "A_MATURITY",
        "Security Short Name": "A_NAME",
        "Security Distribution SEDOL": "A_SEDOL",
        "Category Description": "A_TYPE_CLIENT",
        "Request To Date": "AS_OF_DATE",
        "Account Requested": "R_IDFUND",
        "Shares/Par": "T_AMOUNT",
        "Commission (Base)": "T_COMMISSION",
        "Security Contract Size": "T_CONSIZE",
        "Currency Code-Settle": "T_CURR",
        "Trade Date Exchange Rate": "T_FXRATE",
        "Memo Number": "T_IDINT",
        "Trade Date": "T_TDATE",
        "Realized Capital Gain/Loss": "T_TOTAL_GAIN",
        "Trade Expense (BASE)": "T_TRADE_EXPENSE",
        "Price-Local-Transaction": "T_TRADEPRICE_AC",
        "Price-Base-Transaction": "T_TRADEPRICE_FC",
        "Transaction Code": "T_TYPE_CLIENT",
        "Actual Settle Date": "T_VDATE"
    },
    "code_mappings": {
        "A_TYPE": {},
        "T_TYPE": {}
    },
    "calculated_columns": [
        {
            "name": "A_TYPE",
            "expr": "F.col('A_TYPE_CLIENT')",
            "requires": ["A_TYPE_CLIENT"]
        },
        {
            "name": "T_TYPE",
            "expr": "F.col('T_TYPE_CLIENT')",
            "requires": ["T_TYPE_CLIENT"]
        },
        {
            "name": "A_CURR",
            "expr": "F.upper(F.trim(F.col('A_CURR')))",
            "requires": ["A_CURR"]
        },
        {
            "name": "T_CURR",
            "expr": "F.upper(F.trim(F.col('T_CURR')))",
            "requires": ["T_CURR"]
        },
        {
            "name": "T_IDINT",
            "expr": "F.col('T_IDINT').cast(StringType())",
            "requires": ["T_IDINT"]
        },
        {
            "name": "A_LONGSHORT",
            "expr": "F.when(F.col('A_LONGSHORT').isNull(), F.lit(None).cast(IntegerType()))"
                    ".when(F.upper(F.trim(F.col('A_LONGSHORT').cast(StringType()))).isin(['LONG','L','0']), F.lit(0).cast(IntegerType()))"
                    ".when(F.upper(F.trim(F.col('A_LONGSHORT').cast(StringType()))).isin(['SHORT','S','1']), F.lit(1).cast(IntegerType()))"
                    ".otherwise(F.col('A_LONGSHORT').cast(IntegerType()))",
            "requires": ["A_LONGSHORT"]
        },
        {
            "name": "T_CONSIZE",
            "expr": "F.when(F.col('T_CONSIZE').isNull() | (F.col('T_CONSIZE').cast(DoubleType()) == F.lit(0.0)), F.lit(1))"
                    ".otherwise(F.col('T_CONSIZE'))",
            "requires": ["T_CONSIZE"]
        },
        {
            "name": "T_TOTAL_GAIN",
            "expr": "F.when(F.col('T_TOTAL_GAIN').isNull(), F.lit(0.0))"
                    ".when(F.col('T_TOTAL_GAIN').cast(DoubleType()) > F.lit(0.0), F.col('T_TOTAL_GAIN').cast(DoubleType()))"
                    ".otherwise(F.lit(0.0))",
            "requires": ["T_TOTAL_GAIN"]
        },
        {
            "name": "T_TOTAL_LOSS",
            "expr": "F.when(F.col('T_TOTAL_GAIN').isNull(), F.lit(0.0))"
                    ".when(F.col('T_TOTAL_GAIN').cast(DoubleType()) < F.lit(0.0), F.col('T_TOTAL_GAIN').cast(DoubleType()))"
                    ".otherwise(F.lit(0.0))",
            "requires": ["T_TOTAL_GAIN"]
        }
    ],
    "filters": [
        {
            "desc": "Reject rows where A_CURR is null or not 3 letters",
            "expr": "F.col('A_CURR').isNotNull() & (F.length(F.col('A_CURR')) == 3) & F.col('A_CURR').rlike('^[A-Z]{3}$')",
            "requires": ["A_CURR"]
        },
        {
            "desc": "Reject rows where T_CURR is null or not 3 letters",
            "expr": "F.col('T_CURR').isNotNull() & (F.length(F.col('T_CURR')) == 3) & F.col('T_CURR').rlike('^[A-Z]{3}$')",
            "requires": ["T_CURR"]
        }
    ],
    "require_not_null": [
        "A_BOOKVALUE_AC",
        "A_BOOKVALUE_FC",
        "A_CURR",
        "A_CUSIP",
        "A_IDINT",
        "A_NAME",
        "A_TYPE",
        "AS_OF_DATE",
        "R_IDFUND",
        "T_AMOUNT",
        "T_COMMISSION",
        "T_CONSIZE",
        "T_CURR",
        "T_FXRATE",
        "T_IDINT",
        "T_TDATE",
        "T_TRADE_EXPENSE",
        "T_TRADEPRICE_AC",
        "T_TRADEPRICE_FC",
        "T_TYPE",
        "T_TYPE_CLIENT",
        "T_VDATE"
    ],
    "date_columns": {
        "AS_OF_DATE": "yyyyMMdd",
        "T_TDATE": "yyyyMMdd",
        "T_VDATE": "yyyyMMdd",
        "A_MATURITY": "yyyyMMdd"
    },
    "output_columns": [
        "A_BOOKVALUE_AC",
        "A_BOOKVALUE_FC",
        "A_CURR",
        "A_CUSIP",
        "A_IDINT",
        "A_ISIN",
        "A_LONGSHORT",
        "A_MATURITY",
        "A_NAME",
        "A_SEDOL",
        "A_TYPE",
        "A_TYPE_CLIENT",
        "AS_OF_DATE",
        "R_IDFUND",
        "T_AMOUNT",
        "T_COMMISSION",
        "T_CONSIZE",
        "T_CURR",
        "T_FXRATE",
        "T_IDINT",
        "T_TDATE",
        "T_TOTAL_GAIN",
        "T_TOTAL_LOSS",
        "T_TRADE_EXPENSE",
        "T_TRADEPRICE_AC",
        "T_TRADEPRICE_FC",
        "T_TYPE",
        "T_TYPE_CLIENT",
        "T_VDATE"
    ]
}
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