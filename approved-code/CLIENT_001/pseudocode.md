1. Data Reading and Initial Validation  
1.1 Read the source data file  
1.1.1 Read the **Effective_Transactions** file (the source file named in the mapping).  
1.1.2 Confirm the file contains a single table of transactions with **234 columns** and that key columns referenced below exist exactly as named (including spaces and punctuation).  

1.2 Confirm file structure matches the expected template  
1.2.1 Confirm the following mapped source columns exist in the file (from the mapping sheet):  
- Cost-Local-Transaction  
- Cost-Basis-Transaction  
- Currency Code-Trade  
- Security Number (CUSIP/CINS)  
- SECURITY NUMBER (FULL) *(note: in the data profile the closest field is “Security Number Full”; confirm exact header in the actual file)*  
- Security ISIN  
- Long or Short Position  
- Bond Maturity Date  
- Security Short Name  
- Security Distribution SEDOL  
- Category Description  
- Request To Date  
- Account Requested  
- Shares/Par  
- Commission (Base)  
- Security Contract Size  
- Currency Code-Settle  
- Trade Date Exchange Rate  
- Memo Number  
- Trade Date  
- Realized Capital Gain/Loss  
- Trade Expense (BASE)  
- Price-Local-Transaction  
- Price-Base-Transaction  
- Transaction Code  
- Actual Settle Date  

1.2.2 If any of the above columns are missing or spelled differently, stop and reconcile headers before transforming.

1.3 Profile-driven data type and anomaly notes (from the provided data profile)  
1.3.1 Dates are stored as numbers in **yyyyMMdd** format (examples seen: 20230430, 20230215, 20220610). This applies to:  
- Request To Date, Request From Date, Trade Date, Actual Settle Date, Bond Maturity Date, Effective Date Transaction, Contractual Settle Date, Issue Date, etc.  
1.3.2 Several identifier fields are sometimes null in the sample (important because some are “Critical” in the mapping):  
- Security Number (CUSIP/CINS) has ~10% nulls  
- Security ISIN has ~10% nulls  
- Security Distribution SEDOL has ~10% nulls  
1.3.3 Reversal indicator exists: **TS-REV-FLAG** has ~60% nulls, with observed values **“O”** and **“R”**.  
1.3.4 Many columns are constant or mostly null; they can be ignored unless mapped.

1.4 Validate key fields are not null (list EVERY required field by name)  
Based on the mapping tab “Fund Transactions”, all fields marked **Critical** must be present and non-null unless the mapping notes indicate a conditional rule. Validate these DNAV-required fields after mapping (and validate their source fields before mapping where possible):

Critical DNAV fields and their source columns to check for nulls:  
- A_BOOKVALUE_AC  ← Cost-Local-Transaction  
- A_BOOKVALUE_FC  ← Cost-Basis-Transaction  
- A_CURR          ← Currency Code-Trade *(must not be null; must be 3 letters)*  
- A_CUSIP         ← Security Number (CUSIP/CINS) *(mapping says Critical; data shows some nulls—flag exceptions)*  
- A_IDINT         ← SECURITY NUMBER (FULL) *(must not be null; confirm correct source header)*  
- A_MATURITY      ← Bond Maturity Date *(Critical “for fixed income/derivatives only”; allow null/0 for non-fixed-income if documented)*  
- A_NAME          ← Security Short Name  
- A_TYPE          ← Category Description *(Critical; also requires standardization per A_TYPE tab, but that tab was not provided here)*  
- AS_OF_DATE      ← Request To Date *(must not be null; date format yyyyMMdd in source)*  
- R_IDFUND        ← Account Requested  
- T_AMOUNT        ← Shares/Par  
- T_COMMISSION    ← Commission (Base)  
- T_CONSIZE       ← Security Contract Size *(Critical; mapping note: default to 1 for non-derivatives)*  
- T_CURR          ← Currency Code-Settle *(must not be null; must be 3 letters)*  
- T_FXRATE        ← Trade Date Exchange Rate  
- T_IDINT         ← Memo Number *(must not be null; note: source is numeric in profile—convert to text)*  
- T_TDATE         ← Trade Date *(must not be null; yyyyMMdd in source)*  
- T_TOTAL_GAIN    ← Realized Capital Gain/Loss *(same source as loss; see rule below)*  
- T_TOTAL_LOSS    ← Realized Capital Gain/Loss *(same source as gain; see rule below)*  
- T_TRADE_EXPENSE ← Trade Expense (BASE)  
- T_TRADEPRICE_AC ← Price-Local-Transaction  
- T_TRADEPRICE_FC ← Price-Base-Transaction  
- T_TYPE          ← Transaction Code *(Critical; also requires standardization per T_TYPE tab, but that tab was not provided here)*  
- T_TYPE_CLIENT   ← Transaction Code  
- T_VDATE         ← Actual Settle Date  

1.4.1 If any Critical field is null (and not conditionally allowed), mark the row as invalid and handle per Section 4.5.

1.5 Check date formats and currency code formats present in the source data  
1.5.1 For each date source column used in mapping (Request To Date, Trade Date, Actual Settle Date, Bond Maturity Date):  
- Confirm values are either blank/0 or an 8-digit number in **yyyyMMdd**.  
- Convert to a true date value for output (retain the same calendar date).  
- Treat “0” as missing date (null).  
1.5.2 For currency codes (Currency Code-Trade, Currency Code-Settle):  
- Confirm they are 3-character alphabetic ISO-style codes (examples seen: USD, EUR).  
- Trim spaces and force uppercase.  
- If not 3 letters, flag as invalid.

1.6 Handle duplicate column names if found  
1.6.1 If the input file contains duplicate headers, rename duplicates by appending “(2)”, “(3)”, etc., and document which one is used for mapping.  
1.6.2 Prefer the column whose values match the data profile expectations (e.g., populated vs. all null).

---

2. Column Mapping and Renaming  
2.1 Create the “Fund Transactions” output dataset and map every field exactly as specified  
For each row in Effective_Transactions, create these target fields (DNAV fields) as follows:

2.1.1 Map **'Cost-Local-Transaction'** to **'A_BOOKVALUE_AC'**  
2.1.2 Map **'Cost-Basis-Transaction'** to **'A_BOOKVALUE_FC'**  
2.1.3 Map **'Currency Code-Trade'** to **'A_CURR'**  
2.1.4 Map **'Security Number (CUSIP/CINS)'** to **'A_CUSIP'**  
2.1.5 Map **'SECURITY NUMBER (FULL)'** to **'A_IDINT'** *(confirm exact source header; data profile shows “Security Number Full”)*  
2.1.6 Map **'Security ISIN'** to **'A_ISIN'**  
2.1.7 Map **'Long or Short Position'** to **'A_LONGSHORT'** *(mapping note: “Should be 0 for Long and 1 for Short”; source appears mostly null—see Section 3 for defaulting rule if needed)*  
2.1.8 Map **'Bond Maturity Date'** to **'A_MATURITY'**  
2.1.9 Map **'Security Short Name'** to **'A_NAME'**  
2.1.10 Map **'Security Distribution SEDOL'** to **'A_SEDOL'**  
2.1.11 Map **'Category Description'** to **'A_TYPE'** *(requires A_TYPE tab mapping; not provided)*  
2.1.12 Map **'Category Description'** to **'A_TYPE_CLIENT'**  
2.1.13 Map **'Request To Date'** to **'AS_OF_DATE'**  
2.1.14 Map **'Account Requested'** to **'R_IDFUND'**  
2.1.15 Map **'Shares/Par'** to **'T_AMOUNT'**  
2.1.16 Map **'Commission (Base)'** to **'T_COMMISSION'**  
2.1.17 Map **'Security Contract Size'** to **'T_CONSIZE'** *(mapping note: for non-derivatives default should be 1)*  
2.1.18 Map **'Currency Code-Settle'** to **'T_CURR'**  
2.1.19 Map **'Trade Date Exchange Rate'** to **'T_FXRATE'**  
2.1.20 Map **'Memo Number'** to **'T_IDINT'**  
2.1.21 Map **'Trade Date'** to **'T_TDATE'**  
2.1.22 Map **'Realized Capital Gain/Loss'** to **'T_TOTAL_GAIN'** *(see split rule in Section 3.2)*  
2.1.23 Map **'Realized Capital Gain/Loss'** to **'T_TOTAL_LOSS'** *(see split rule in Section 3.2)*  
2.1.24 Map **'Trade Expense (BASE)'** to **'T_TRADE_EXPENSE'**  
2.1.25 Map **'Price-Local-Transaction'** to **'T_TRADEPRICE_AC'**  
2.1.26 Map **'Price-Base-Transaction'** to **'T_TRADEPRICE_FC'**  
2.1.27 Map **'Transaction Code'** to **'T_TYPE'** *(requires T_TYPE tab mapping; not provided)*  
2.1.28 Map **'Transaction Code'** to **'T_TYPE_CLIENT'**  
2.1.29 Map **'Actual Settle Date'** to **'T_VDATE'**

2.2 Lookup/reference tab rules (not provided in the input)  
2.2.1 The mapping sheet explicitly says:  
- “A_TYPE: Important Note: Please complete mapping in A_TYPE tab”  
- “T_TYPE: Important Note: Please complete mapping in T_TYPE tab”  
2.2.2 Because the A_TYPE and T_TYPE lookup tabs were not included in the provided mapping extract, do the following until those tabs are supplied:  
- Output **A_TYPE_CLIENT** and **T_TYPE_CLIENT** exactly as the client/source values.  
- For **A_TYPE** and **T_TYPE**, temporarily pass through the same source values, and flag that final standardization cannot be completed without the lookup tabs.  
2.2.3 Once lookup tabs are provided, replace pass-through with the explicit code mapping rules from those tabs (e.g., map “BUY” → “DT_BUY”, etc., if that is what the tab specifies).

---

3. Calculations and Derived Columns  
3.1 Date conversions (derived formatting)  
3.1.1 Convert **AS_OF_DATE** from numeric yyyyMMdd (Request To Date) into a proper date. Treat 0 as null.  
3.1.2 Convert **T_TDATE** from numeric yyyyMMdd (Trade Date) into a proper date. Treat 0 as null.  
3.1.3 Convert **T_VDATE** from numeric yyyyMMdd (Actual Settle Date) into a proper date. Treat 0 as null.  
3.1.4 Convert **A_MATURITY** from numeric yyyyMMdd (Bond Maturity Date) into a proper date. Treat 0 as null.

3.2 Realized gain/loss split into two DNAV fields (explicit rule from mapping notes)  
The mapping provides two DNAV fields fed by the same source column “Realized Capital Gain/Loss”, with notes:  
- T_TOTAL_GAIN: “Positive for Total Gain”  
- T_TOTAL_LOSS: “Negative for Total Loss”  

Apply this rule:  
3.2.1 If **Realized Capital Gain/Loss > 0**:  
- Set **T_TOTAL_GAIN = Realized Capital Gain/Loss**  
- Set **T_TOTAL_LOSS = 0**  
3.2.2 If **Realized Capital Gain/Loss < 0**:  
- Set **T_TOTAL_GAIN = 0**  
- Set **T_TOTAL_LOSS = Realized Capital Gain/Loss** *(keep it negative, as the mapping example shows negative loss)*  
3.2.3 If **Realized Capital Gain/Loss = 0 or null**:  
- Set both **T_TOTAL_GAIN = 0** and **T_TOTAL_LOSS = 0** (or nulls if DNAV allows; mapping does not specify, so default to 0 for numeric fields).

3.3 Contract size defaulting (per mapping note)  
3.3.1 If **Security Contract Size** is null, zero, or missing for a row that is not a derivative, set **T_CONSIZE = 1**.  
3.3.2 If the transaction is a derivative (cannot be determined from provided mapping alone), keep the provided contract size and flag if missing.

3.4 Long/Short indicator standardization (per mapping note)  
3.4.1 Mapping note says: “Should be 0 for Long and 1 For Short”.  
3.4.2 If source **Long or Short Position** contains text values (e.g., “Long”, “Short”), convert them to 0/1 accordingly.  
3.4.3 If source is null (as in the profile sample), leave **A_LONGSHORT** null and flag as Non-Critical missing (since A_LONGSHORT is Non-Critical).

3.5 Amortization, NAV, and other derived fields  
3.5.1 The provided mapping extract for “Fund Transactions” does **not** include DNAV fields for NAV or amortization calculations.  
3.5.2 Therefore, do **not** calculate NAV, amortization, or additional derived metrics unless they appear in other mapping tabs/sheets (not provided here).  
3.5.3 If additional mapping tabs are later provided (e.g., holdings, balances, NAV summary), add those formulas exactly as specified there.

---

4. Filtering and Business Rules  
4.1 Remove or flag rows missing required identifiers  
4.1.1 If **R_IDFUND (Account Requested)** is null → reject the row (cannot assign to a fund).  
4.1.2 If **T_IDINT (Memo Number)** is null → reject the row (cannot uniquely identify transaction).  
4.1.3 If **AS_OF_DATE (Request To Date)** is null/0 → reject the row (as-of date is critical).  
4.1.4 If **T_TDATE (Trade Date)** is null/0 → reject the row (trade date is critical).  
4.1.5 If **T_CURR (Currency Code-Settle)** is null or not 3 letters → reject the row.  
4.1.6 If **A_CURR (Currency Code-Trade)** is null or not 3 letters → reject the row.  
4.1.7 If **A_IDINT (SECURITY NUMBER (FULL))** is null → reject the row (critical investment identifier).  
4.1.8 If **A_CUSIP (Security Number (CUSIP/CINS))** is null → mapping marks it Critical; reject or quarantine depending on auditor decision (recommend quarantine because sample shows some nulls).  

4.2 Reversal transaction handling  
4.2.1 Use **TS-REV-FLAG** as the reversal indicator (present in the data profile).  
4.2.2 Treat rows where **TS-REV-FLAG = 'R'** as reversal rows.  
4.2.3 Treat rows where **TS-REV-FLAG = 'O'** as original rows that have a related reversal (based on observed values; confirm meaning with client).  
4.2.4 Recommended handling (until client confirms exact semantics):  
- Keep both original and reversal rows in the output, but add an internal audit note/report that identifies reversal pairs using:  
  - Reversal Cross Reference Memo Number  
  - Rebook Cross Reference Memo  
  - Memo Number  
4.2.5 If DNAV requires reversals to be excluded or netted, apply that rule only after it is confirmed in the mapping/checklist tabs (not provided).

4.3 Transaction type filtering (if applicable)  
4.3.1 No explicit “VOID”/“CANCELLED” status field or filter rule was provided in the mapping extract.  
4.3.2 Therefore, do not filter by status unless another mapping tab/checklist specifies it.

4.4 Account class filtering (if applicable)  
4.4.1 No account class filter rules were provided in the mapping extract.  
4.4.2 Therefore, do not filter by account class unless another mapping tab/checklist specifies it.

4.5 Duplicate transaction handling  
4.5.1 Check for duplicate **T_IDINT (Memo Number)** within the same **R_IDFUND (Account Requested)** and **T_TDATE (Trade Date)**.  
4.5.2 If duplicates exist:  
- Keep one row only if all mapped values are identical.  
- Otherwise, quarantine duplicates for auditor review.

---

5. Output Format and Destination  
5.1 Output tabs/files required  
5.1.1 Create an output file (or workbook tab) named exactly: **Fund Transactions**.

5.2 Required column naming and order  
5.2.1 Output the columns in the DNAV Field order shown in the mapping sheet (top to bottom):  
1) A_BOOKVALUE_AC  
2) A_BOOKVALUE_FC  
3) A_CURR  
4) A_CUSIP  
5) A_IDINT  
6) A_ISIN  
7) A_LONGSHORT  
8) A_MATURITY  
9) A_NAME  
10) A_SEDOL  
11) A_TYPE  
12) A_TYPE_CLIENT  
13) AS_OF_DATE  
14) R_IDFUND  
15) T_AMOUNT  
16) T_COMMISSION  
17) T_CONSIZE  
18) T_CURR  
19) T_FXRATE  
20) T_IDINT  
21) T_TDATE  
22) T_TOTAL_GAIN  
23) T_TOTAL_LOSS  
24) T_TRADE_EXPENSE  
25) T_TRADEPRICE_AC  
26) T_TRADEPRICE_FC  
27) T_TYPE  
28) T_TYPE_CLIENT  
29) T_VDATE  

5.3 Data integrity checks to run before delivery (based on mapping requirements and observed profile)  
5.3.1 Confirm all **Critical** DNAV fields listed in Section 1.4 are populated (or conditionally allowed) for all output rows.  
5.3.2 Confirm currency codes are uppercase 3-letter codes for **A_CURR** and **T_CURR**.  
5.3.3 Confirm date fields are valid dates after conversion (no impossible dates).  
5.3.4 Confirm **T_TOTAL_GAIN** is never negative and **T_TOTAL_LOSS** is never positive (per the mapping notes).  
5.3.5 Provide an exceptions report listing rows rejected/quarantined with the reason (missing critical field, invalid date, invalid currency, duplicate transaction ID, etc.).  

5.4 Open items / dependencies (must be resolved to be “complete”)  
5.4.1 Provide the missing mapping workbook tabs for **A_TYPE** and **T_TYPE** so transaction and asset type codes can be standardized to DNAV-required values (the mapping explicitly references these tabs).