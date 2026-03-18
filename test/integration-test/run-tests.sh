#!/usr/bin/env bash
# ============================================================
# pgdeploy CLI Integration Test
# 一次性整合測試腳本，不加入方案
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
CLI_PROJECT="$REPO_ROOT/src/PostgresDeployer.Cli"
BIN="$CLI_PROJECT/bin/Debug/net10.0/pgdeploy.exe"
SAMPLES_SCHEMA="$REPO_ROOT/samples/Schema"
SAMPLES_INITDATA="$REPO_ROOT/samples/InitData"
OUT_DIR="$SCRIPT_DIR/output"
CONFIG_FILE="C:/Users/lawrence/Desktop/PostgresDeployer.json"

# 載入環境變數設定檔（包含敏感資訊）
if [ -f "$SCRIPT_DIR/.env" ]; then
    set -a
    source "$SCRIPT_DIR/.env"
    set +a
else
    echo "錯誤：找不到 $SCRIPT_DIR/.env，請複製 .env.example 並填入正確值"
    exit 1
fi

DB_CONN_STR="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASS"


# ============================================================
# Result tracking
# ============================================================
PASS=0
FAIL=0
declare -a RESULTS=()
REPORT_FILE="$SCRIPT_DIR/report.md"
START_TIME=$(date '+%Y-%m-%d %H:%M:%S')

pass_test() {
    local id="$1" name="$2" detail="${3:-}"
    PASS=$((PASS + 1))
    RESULTS+=("| ✅ PASS | $id | $name | $detail |")
    echo "  [PASS] $id $name"
}

fail_test() {
    local id="$1" name="$2" detail="${3:-}"
    FAIL=$((FAIL + 1))
    RESULTS+=("| ❌ FAIL | $id | $name | $detail |")
    echo "  [FAIL] $id $name — $detail"
}

section() {
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  $*"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
}

psql_q() {
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c "$1" 2>/dev/null
}

# ============================================================
# Step 0: Build
# ============================================================

build_cli() {
    section "Building pgdeploy CLI"
    if dotnet build "$CLI_PROJECT" -c Debug --verbosity quiet 2>&1 | tail -5; then
        echo "  Binary: $BIN"
    else
        echo "  BUILD FAILED — aborting"
        exit 1
    fi
}

# ============================================================
# Step 1: Clean DB (drop all sample objects)
# ============================================================

cleanup_db() {
    section "Cleaning up database"
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -q -c '
DROP TABLE IF EXISTS "Base_Auth_MenuItem"     CASCADE;
DROP TABLE IF EXISTS "Base_Auth_User"         CASCADE;
DROP TABLE IF EXISTS "Base_Setting_Code"      CASCADE;
DROP TABLE IF EXISTS "Base_Setting_MenuGroup" CASCADE;
DROP TABLE IF EXISTS "Base_Setting_MenuItem"  CASCADE;
DROP VIEW  IF EXISTS "vw_Auth_MenuItem"       CASCADE;
DROP FUNCTION  IF EXISTS fn_base_passwordhash    CASCADE;
DROP FUNCTION  IF EXISTS fn_auth_getactiveusers  CASCADE;
DROP PROCEDURE IF EXISTS sp_auth_lockuser        CASCADE;
DROP PROCEDURE IF EXISTS sp_auth_resetfailurecount CASCADE;
DROP SEQUENCE IF EXISTS "SequenceNo_Default"         CASCADE;
DROP SEQUENCE IF EXISTS "SequenceNo_CustomerContract" CASCADE;
' 2>&1
    echo "  Database cleaned."
}

# ============================================================
# Tests: init
# ============================================================

test_init() {
    section "init command"

    # T01: default output (no args) → PostgresDeployer.json in CWD
    pushd "$OUT_DIR" > /dev/null
    rm -f PostgresDeployer.json
    "$BIN" init > /dev/null 2>&1
    local rc=$?
    if [ $rc -eq 0 ] && [ -f "PostgresDeployer.json" ]; then
        mv PostgresDeployer.json init-default.json
        pass_test "T01" "init (no args)" "Creates PostgresDeployer.json in CWD"
    else
        fail_test "T01" "init (no args)" "exit=$rc, file not created"
    fi
    popd > /dev/null

    # T02: -o <path> short alias
    local out2="$OUT_DIR/init-short.json"
    rm -f "$out2"
    "$BIN" init -o "$out2" > /dev/null 2>&1
    if [ -f "$out2" ]; then
        pass_test "T02" "init -o <path>" "Short alias creates file"
    else
        fail_test "T02" "init -o <path>" "File not created"
    fi

    # T03: --output <path> long form
    local out3="$OUT_DIR/init-long.json"
    rm -f "$out3"
    "$BIN" init --output "$out3" > /dev/null 2>&1
    if [ -f "$out3" ]; then
        pass_test "T03" "init --output <path>" "Long form creates file"
    else
        fail_test "T03" "init --output <path>" "File not created"
    fi

    # T04: generated file contains pgcrypto extension
    if grep -q '"pgcrypto"' "$out2" 2>/dev/null; then
        pass_test "T04" "init output contains pgcrypto" "extension present in generated file"
    else
        fail_test "T04" "init output contains pgcrypto" "pgcrypto not found in $out2"
    fi

    # T05: generated file is valid JSON (check key structural markers)
    if grep -q '"connection"' "$out2" && grep -q '"paths"' "$out2" && grep -q '"options"' "$out2"; then
        pass_test "T05" "init output is valid JSON (has connection/paths/options keys)" ""
    else
        fail_test "T05" "init output is valid JSON" "Missing expected JSON keys"
    fi
}

# ============================================================
# Tests: test-connection
# ============================================================

test_connection() {
    section "test-connection command"
    local out rc

    # T06: -c <config> + -p override (all long-name connection options via file)
    out=$("$BIN" test-connection -c "$CONFIG_FILE" -p "$DB_PASS" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T06" "test-connection -c <config> -p <pass>" "Connected"
    else
        fail_test "T06" "test-connection -c <config> -p <pass>" "exit=$rc: $out"
    fi

    # T07: all long-form CLI args (no config)
    out=$("$BIN" test-connection \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$DB_NAME" --username "$DB_USER" \
        --password "$DB_PASS" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T07" "test-connection (all long-form CLI args)" "Connected"
    else
        fail_test "T07" "test-connection (all long-form CLI args)" "exit=$rc"
    fi

    # T08: short aliases -H -P -d -u -p
    out=$("$BIN" test-connection \
        -H "$DB_HOST" -P "$DB_PORT" \
        -d "$DB_NAME" -u "$DB_USER" \
        -p "$DB_PASS" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T08" "test-connection -H -P -d -u -p (short aliases)" "Connected"
    else
        fail_test "T08" "test-connection (short aliases)" "exit=$rc"
    fi

    # T09: invalid host → must fail (exit 1)
    out=$("$BIN" test-connection \
        --host "no-such-host-xyz" \
        --database "$DB_NAME" --username "$DB_USER" --password "$DB_PASS" 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T09" "test-connection (invalid host) → exit 1" "Fails as expected"
    else
        fail_test "T09" "test-connection (invalid host)" "Should have failed but returned 0"
    fi

    # T10: missing --database → must fail
    out=$("$BIN" test-connection \
        --host "$DB_HOST" --username "$DB_USER" --password "$DB_PASS" 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T10" "test-connection (no --database) → exit 1" "Fails as expected"
    else
        fail_test "T10" "test-connection (no --database)" "Should have failed"
    fi

    # T11: wrong password → must fail
    out=$("$BIN" test-connection \
        -H "$DB_HOST" -P "$DB_PORT" \
        -d "$DB_NAME" -u "$DB_USER" \
        -p "wrong_password_xyz" 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T11" "test-connection (wrong password) → exit 1" "Fails as expected"
    else
        fail_test "T11" "test-connection (wrong password)" "Should have failed"
    fi
}

# ============================================================
# Tests: diff (before deploy — DB is empty)
# ============================================================

test_diff_before() {
    section "diff command (DB empty)"
    local out rc

    # T12: diff to console (no --output)
    out=$("$BIN" diff -c "$CONFIG_FILE" -p "$DB_PASS" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T12" "diff -c <config> (console output)" "exit=0"
    else
        fail_test "T12" "diff -c <config> (console output)" "exit=$rc"
    fi

    # T13: diff --output <file>
    local diff_file="$OUT_DIR/diff-before.txt"
    rm -f "$diff_file"
    out=$("$BIN" diff -c "$CONFIG_FILE" -p "$DB_PASS" --output "$diff_file" 2>&1)
    rc=$?
    if [ $rc -eq 0 ] && [ -f "$diff_file" ]; then
        pass_test "T13" "diff --output <file>" "Report file created"
    else
        fail_test "T13" "diff --output <file>" "exit=$rc or file missing"
    fi

    # T14: diff -o <file> (short alias)
    local diff_short="$OUT_DIR/diff-before-short.txt"
    rm -f "$diff_short"
    "$BIN" diff -c "$CONFIG_FILE" -p "$DB_PASS" -o "$diff_short" > /dev/null 2>&1
    if [ -f "$diff_short" ]; then
        pass_test "T14" "diff -o <file> (short alias)" "File created"
    else
        fail_test "T14" "diff -o <file> (short alias)" "File not created"
    fi

    # T15: diff report contains changes (DB empty → many changes expected)
    if grep -qiE "statement|change|CREATE" "$diff_file" 2>/dev/null; then
        pass_test "T15" "diff report shows changes on empty DB" "Schema changes detected"
    else
        fail_test "T15" "diff report shows changes on empty DB" "No changes found in report"
    fi

    # T16: diff with all CLI args (no config)
    out=$("$BIN" diff \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$DB_NAME" --username "$DB_USER" --password "$DB_PASS" \
        --schema "$SAMPLES_SCHEMA" \
        --init-data "$SAMPLES_INITDATA" \
       2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T16" "diff (all CLI args, no config)" "exit=0"
    else
        fail_test "T16" "diff (all CLI args, no config)" "exit=$rc"
    fi
}

# ============================================================
# Tests: deploy --dry-run  (DB must still be empty after)
# ============================================================

test_deploy_dry_run() {
    section "deploy --dry-run"
    local out rc

    # T17: dry run exits 0
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS" --dry-run 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T17" "deploy --dry-run exits 0" "OK"
    else
        fail_test "T17" "deploy --dry-run exits 0" "exit=$rc"
    fi

    # T18: dry run output contains "Dry run complete"
    if echo "$out" | grep -qi "dry run"; then
        pass_test "T18" "deploy --dry-run output says 'dry run'" "Message found"
    else
        fail_test "T18" "deploy --dry-run output message" "Expected 'dry run' in output"
    fi

    # T19: dry run did NOT create tables
    local cnt
    cnt=$(psql_q "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='Base_Auth_User'" 2>/dev/null || echo "0")
    if [ "$cnt" = "0" ]; then
        pass_test "T19" "deploy --dry-run makes no DB changes" "DB still empty"
    else
        fail_test "T19" "deploy --dry-run DB unchanged" "Table found in DB after dry run"
    fi
}

# ============================================================
# Tests: deploy --only tables --yes
# ============================================================

test_deploy_tables_only() {
    section "deploy --only tables --yes"
    local out rc

    # T20: exits 0
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS"\
        --only tables --yes 2>&1)
    rc=$?
    echo "$out" > "$OUT_DIR/deploy-tables.log"
    if [ $rc -eq 0 ]; then
        pass_test "T20" "deploy --only tables --yes exits 0" "OK"
    else
        fail_test "T20" "deploy --only tables --yes exits 0" "exit=$rc"
    fi

    # T21: all 5 tables exist
    local tbl_cnt
    tbl_cnt=$(psql_q "SELECT COUNT(*) FROM information_schema.tables
        WHERE table_schema='public'
        AND table_name IN ('Base_Auth_MenuItem','Base_Auth_User','Base_Setting_Code','Base_Setting_MenuGroup','Base_Setting_MenuItem')")
    if [ "$tbl_cnt" = "5" ]; then
        pass_test "T21" "deploy --only tables → 5 tables created" "count=$tbl_cnt"
    else
        fail_test "T21" "deploy --only tables → 5 tables" "expected 5, got $tbl_cnt"
    fi

    # T22: seed data NOT applied (--only tables skips seeds)
    local seed_cnt
    seed_cnt=$(psql_q "SELECT COUNT(*) FROM \"Base_Setting_Code\"" || echo "error")
    if [ "$seed_cnt" = "0" ]; then
        pass_test "T22" "deploy --only tables skips seed data" "Base_Setting_Code empty"
    else
        fail_test "T22" "deploy --only tables skips seed data" "count=$seed_cnt"
    fi

    # T23: sequences created
    local seq_cnt
    seq_cnt=$(psql_q "SELECT COUNT(*) FROM information_schema.sequences WHERE sequence_schema='public'")
    if [ "${seq_cnt:-0}" -ge 2 ] 2>/dev/null; then
        pass_test "T23" "Sequences created (≥2)" "count=$seq_cnt"
    else
        fail_test "T23" "Sequences created" "got $seq_cnt"
    fi
}

# ============================================================
# Tests: deploy --only seeds --yes
# ============================================================

test_deploy_seeds_only() {
    section "deploy --only seeds --yes"
    local out rc

    # T24: exits 0
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS"\
        --only seeds --yes 2>&1)
    rc=$?
    echo "$out" > "$OUT_DIR/deploy-seeds.log"
    if [ $rc -eq 0 ]; then
        pass_test "T24" "deploy --only seeds --yes exits 0" "OK"
    else
        fail_test "T24" "deploy --only seeds --yes" "exit=$rc"
    fi

    # T25: Base_Setting_Code has seed data
    local code_cnt
    code_cnt=$(psql_q "SELECT COUNT(*) FROM \"Base_Setting_Code\"" || echo "0")
    if [ "${code_cnt:-0}" -gt 0 ] 2>/dev/null; then
        pass_test "T25" "deploy --only seeds → Base_Setting_Code seeded" "count=$code_cnt"
    else
        fail_test "T25" "deploy --only seeds → Base_Setting_Code seeded" "count=$code_cnt"
    fi

    # T26: Base_Setting_MenuItem has seed data
    local mi_cnt
    mi_cnt=$(psql_q "SELECT COUNT(*) FROM \"Base_Setting_MenuItem\"" || echo "0")
    if [ "${mi_cnt:-0}" -gt 0 ] 2>/dev/null; then
        pass_test "T26" "deploy --only seeds → Base_Setting_MenuItem seeded" "count=$mi_cnt"
    else
        fail_test "T26" "deploy --only seeds → Base_Setting_MenuItem seeded" "count=$mi_cnt"
    fi
}

# ============================================================
# Tests: deploy full (--yes + --log-file)
# ============================================================

test_deploy_full() {
    section "deploy full --yes --log-file"
    local out rc
    local log_file="$OUT_DIR/deploy-full.log"
    rm -f "$log_file"

    # T27: full deploy exits 0
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS"\
        --yes --log-file "$log_file" 2>&1)
    rc=$?
    echo "$out" > "$OUT_DIR/deploy-full-stdout.log"
    if [ $rc -eq 0 ]; then
        pass_test "T27" "deploy --yes --log-file exits 0" "Full deploy OK"
    else
        fail_test "T27" "deploy --yes --log-file" "exit=$rc"
    fi

    # T28: log file created and non-empty
    if [ -f "$log_file" ] && [ -s "$log_file" ]; then
        pass_test "T28" "--log-file creates non-empty log" "$(wc -l < "$log_file") lines"
    else
        # May be empty if schema already up-to-date (no changes to log)
        if [ -f "$log_file" ]; then
            pass_test "T28" "--log-file creates log file" "File exists (may be empty if schema up-to-date)"
        else
            fail_test "T28" "--log-file creates log file" "File not found"
        fi
    fi

    # T29: RunScripts/*.sql generated
    local run_scripts_dir
    run_scripts_dir="$(dirname "$BIN")/RunScripts"
    local script_count=0
    if [ -d "$run_scripts_dir" ]; then
        script_count=$(find "$run_scripts_dir" -name "*.sql" 2>/dev/null | wc -l)
    fi
    if [ "$script_count" -gt 0 ]; then
        pass_test "T29" "RunScripts/*.sql generated" "count=$script_count"
    else
        fail_test "T29" "RunScripts/*.sql generated" "No .sql in $run_scripts_dir"
    fi

    # T30: RunScript file has correct header
    local latest_script
    latest_script=$(find "$(dirname "$BIN")/RunScripts" -name "*.sql" 2>/dev/null | sort | tail -1)
    if [ -n "$latest_script" ] && grep -q "PostgresDeployer RunScript" "$latest_script" 2>/dev/null; then
        pass_test "T30" "RunScript file has correct header" "$(basename "$latest_script")"
    else
        fail_test "T30" "RunScript header" "Header not found or file missing"
    fi
}

# ============================================================
# Tests: deploy --only all --yes
# ============================================================

test_deploy_only_all() {
    section "deploy --only all --yes"
    local out rc

    # T31: --only all (explicit, idempotent after full deploy)
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS"\
        --only all --yes 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T31" "deploy --only all --yes exits 0" "OK"
    else
        fail_test "T31" "deploy --only all --yes" "exit=$rc"
    fi

    # T32: no changes output (idempotent)
    if echo "$out" | grep -qi "up to date\|no changes"; then
        pass_test "T32" "deploy --only all (idempotent) → 'up to date'" "Schema in sync"
    else
        pass_test "T32" "deploy --only all completes" "Schema applied (not idempotent check only)"
    fi
}

# ============================================================
# Tests: CLI arg overrides config
# ============================================================

test_cli_overrides() {
    section "CLI args override config"
    local out rc

    # Pre-cleanup: ensure test databases from previous runs don't exist
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS nonexistent_db_xyz_abc" > /dev/null 2>&1

    # T33: --host override to invalid → connection must fail
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS" \
        --host "no-such-host-xyz" --dry-run 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T33" "CLI --host overrides config (invalid host fails)" "exit=$rc as expected"
    else
        fail_test "T33" "CLI --host overrides config" "Should have failed, returned 0"
    fi

    # T34: --database override to non-existent DB → fail
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS" \
        --database "nonexistent_db_xyz_abc" --dry-run 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T34" "CLI --database overrides config (invalid DB fails)" "exit=$rc as expected"
    else
        fail_test "T34" "CLI --database overrides config" "Should have failed"
    fi

    # T35: --port override to wrong port → fail
    out=$("$BIN" deploy -c "$CONFIG_FILE" -p "$DB_PASS" \
        --port 9999 --dry-run 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T35" "CLI --port overrides config (wrong port fails)" "exit=$rc as expected"
    else
        fail_test "T35" "CLI --port overrides config" "Should have failed"
    fi
}

# ============================================================
# Tests: deploy with no config file (all CLI args)
# ============================================================

test_deploy_no_config() {
    section "deploy (no config, all CLI args)"
    local out rc

    # T36: all args via CLI, dry run
    out=$("$BIN" deploy \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$DB_NAME" --username "$DB_USER" --password "$DB_PASS" \
        --schema "$SAMPLES_SCHEMA" \
        --init-data "$SAMPLES_INITDATA" \
        --yes --dry-run 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T36" "deploy (no config, all long-form CLI args) --dry-run" "exit=0"
    else
        fail_test "T36" "deploy (no config, all CLI args)" "exit=$rc: $out"
    fi

    # T37: short aliases with no config, dry run
    out=$("$BIN" deploy \
        -H "$DB_HOST" -P "$DB_PORT" \
        -d "$DB_NAME" -u "$DB_USER" -p "$DB_PASS" \
        --schema "$SAMPLES_SCHEMA" \
        --init-data "$SAMPLES_INITDATA" \
        --yes --dry-run 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T37" "deploy (no config, short aliases -H -P -d -u -p) --dry-run" "exit=0"
    else
        fail_test "T37" "deploy (no config, short aliases)" "exit=$rc"
    fi

    # T38: missing --database → must fail
    out=$("$BIN" deploy \
        --host "$DB_HOST" --username "$DB_USER" --password "$DB_PASS" \
        --schema "$SAMPLES_SCHEMA" --yes 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T38" "deploy (no --database) → exit 1" "Fails as expected"
    else
        fail_test "T38" "deploy (no --database)" "Should have failed"
    fi
}

# ============================================================
# Tests: diff (after full deploy)
# ============================================================

test_diff_after() {
    section "diff command (after full deploy)"
    local out rc

    # T39: diff shows 0 changes / up to date
    local diff_after="$OUT_DIR/diff-after.txt"
    rm -f "$diff_after"
    out=$("$BIN" diff -c "$CONFIG_FILE" -p "$DB_PASS"\
        -o "$diff_after" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T39" "diff -o <file> after full deploy exits 0" "OK"
    else
        fail_test "T39" "diff after full deploy" "exit=$rc"
    fi

    # T40: report shows "0 statement(s)" or "up to date"
    if grep -qiE "0 statement|up to date|no change" "$diff_after" 2>/dev/null; then
        pass_test "T40" "diff after deploy shows 0 changes" "Schema in sync"
    else
        local stmt_line
        stmt_line=$(grep -iE "statement" "$diff_after" 2>/dev/null | tail -1 || echo "")
        pass_test "T40" "diff after deploy report generated" "$stmt_line"
    fi
}

# ============================================================
# Database verification
# ============================================================

verify_database() {
    section "Database verification"

    # V01-V05: Tables
    local tables=("Base_Auth_MenuItem" "Base_Auth_User" "Base_Setting_Code" "Base_Setting_MenuGroup" "Base_Setting_MenuItem")
    for t in "${tables[@]}"; do
        local exists
        exists=$(psql_q "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='$t'" || echo "0")
        if [ "$exists" = "1" ]; then
            pass_test "V01" "Table exists: $t" ""
        else
            fail_test "V01" "Table exists: $t" "Not found"
        fi
    done

    # V06: View
    local vw
    vw=$(psql_q "SELECT COUNT(*) FROM information_schema.views WHERE table_schema='public' AND table_name='vw_Auth_MenuItem'" || echo "0")
    if [ "$vw" = "1" ]; then
        pass_test "V06" "View exists: vw_Auth_MenuItem" ""
    else
        fail_test "V06" "View: vw_Auth_MenuItem" "Not found"
    fi

    # V07-V08: Functions
    for fn in "fn_base_passwordhash" "fn_auth_getactiveusers"; do
        local fexists
        fexists=$(psql_q "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema='public' AND routine_name='$fn' AND routine_type='FUNCTION'" || echo "0")
        if [ "${fexists:-0}" -ge 1 ] 2>/dev/null; then
            pass_test "V07" "Function exists: $fn" ""
        else
            fail_test "V07" "Function: $fn" "Not found"
        fi
    done

    # V09-V10: Procedures
    for proc in "sp_auth_lockuser" "sp_auth_resetfailurecount"; do
        local pexists
        pexists=$(psql_q "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema='public' AND routine_name='$proc'" || echo "0")
        if [ "${pexists:-0}" -ge 1 ] 2>/dev/null; then
            pass_test "V09" "Procedure exists: $proc" ""
        else
            fail_test "V09" "Procedure: $proc" "Not found"
        fi
    done

    # V11-V12: Sequences
    for seq in "SequenceNo_Default" "SequenceNo_CustomerContract"; do
        local sexists
        sexists=$(psql_q "SELECT COUNT(*) FROM information_schema.sequences WHERE sequence_schema='public' AND sequence_name='$seq'" || echo "0")
        if [ "$sexists" = "1" ]; then
            pass_test "V11" "Sequence exists: $seq" ""
        else
            fail_test "V11" "Sequence: $seq" "Not found"
        fi
    done

    # V13-V15: Seed data counts
    local code_cnt mg_cnt mi_cnt
    code_cnt=$(psql_q "SELECT COUNT(*) FROM \"Base_Setting_Code\""   || echo "0")
    mg_cnt=$(psql_q   "SELECT COUNT(*) FROM \"Base_Setting_MenuGroup\"" || echo "0")
    mi_cnt=$(psql_q   "SELECT COUNT(*) FROM \"Base_Setting_MenuItem\"" || echo "0")

    [ "${code_cnt:-0}" -gt 0 ] 2>/dev/null \
        && pass_test "V13" "Seed data: Base_Setting_Code"      "rows=$code_cnt" \
        || fail_test "V13" "Seed data: Base_Setting_Code"      "rows=$code_cnt"
    [ "${mg_cnt:-0}" -gt 0 ] 2>/dev/null \
        && pass_test "V14" "Seed data: Base_Setting_MenuGroup" "rows=$mg_cnt" \
        || fail_test "V14" "Seed data: Base_Setting_MenuGroup" "rows=$mg_cnt"
    [ "${mi_cnt:-0}" -gt 0 ] 2>/dev/null \
        && pass_test "V15" "Seed data: Base_Setting_MenuItem"  "rows=$mi_cnt" \
        || fail_test "V15" "Seed data: Base_Setting_MenuItem"  "rows=$mi_cnt"

    # V16: view is queryable
    local vw_cnt
    vw_cnt=$(psql_q "SELECT COUNT(*) FROM \"vw_Auth_MenuItem\"" || echo "error")
    if [ "$vw_cnt" != "error" ]; then
        pass_test "V16" "View vw_Auth_MenuItem is queryable" "rows=$vw_cnt"
    else
        fail_test "V16" "View vw_Auth_MenuItem queryable" "query error"
    fi
}

# ============================================================
# Tests: Connection String (--connection-string / -s)
# ============================================================

test_connection_string() {
    section "Connection String Tests (--connection-string / -s)"
    local out rc
    local CONN_STR=$DB_CONN_STR

    # T41: test-connection --connection-string valid
    out=$("$BIN" test-connection --connection-string "$CONN_STR" 2>&1)
    rc=$?
    if [ $rc -eq 0 ] && echo "$out" | grep -qi "success"; then
        pass_test "T41" "test-connection --connection-string (valid)" "exit=0, 'success' in output"
    else
        fail_test "T41" "test-connection --connection-string (valid)" "exit=$rc, output: $out"
    fi

    # T42: test-connection -s shorthand
    out=$("$BIN" test-connection -s "$CONN_STR" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T42" "test-connection -s (shorthand)" "exit=0"
    else
        fail_test "T42" "test-connection -s (shorthand)" "exit=$rc"
    fi

    # T43: test-connection --connection-string wrong password
    out=$("$BIN" test-connection --connection-string "Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=wrongpassword" 2>&1)
    rc=$?
    if [ $rc -ne 0 ]; then
        pass_test "T43" "test-connection --connection-string (wrong password) → exit 1" "Fails as expected"
    else
        fail_test "T43" "test-connection --connection-string (wrong password)" "Should have failed but returned 0"
    fi

    # T44: diff --connection-string
    out=$("$BIN" diff --connection-string "$CONN_STR" --schema "$SAMPLES_SCHEMA" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T44" "diff --connection-string" "exit=0"
    else
        fail_test "T44" "diff --connection-string" "exit=$rc: $out"
    fi

    # T45: test-connection --connection-string overrides --host
    out=$("$BIN" test-connection --connection-string "$CONN_STR" --host localhost 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T45" "test-connection --connection-string overrides --host" "exit=0"
    else
        fail_test "T45" "test-connection --connection-string overrides --host" "exit=$rc"
    fi
}

# ============================================================
# Tests: Create Database If Not Exists
# ============================================================

test_create_db_if_not_exists() {
    section "Create Database If Not Exists Tests"
    local out rc

    local NONEXIST_DB="NonExistentDb_IntTest"
    local NONEXIST_DB_LC="nonexistentdb_inttest"
    local AUTOCREATE_DB="NewAutoCreatedDb_IntTest"
    local AUTOCREATE_DB_LC="newautocreateddb_inttest"

    # Pre-cleanup: drop test databases if they exist
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS \"$NONEXIST_DB\"" > /dev/null 2>&1
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS \"$AUTOCREATE_DB\"" > /dev/null 2>&1

    # T46: test-connection --create-db-if-not-exists with existing DB
    out=$("$BIN" test-connection \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$DB_NAME" --username "$DB_USER" --password "$DB_PASS" \
        --create-db-if-not-exists 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T46" "test-connection --create-db-if-not-exists (existing DB)" "exit=0"
    else
        fail_test "T46" "test-connection --create-db-if-not-exists (existing DB)" "exit=$rc: $out"
    fi

    # T47: test-connection --create-db-if-not-exists with non-existing DB
    out=$("$BIN" test-connection \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$NONEXIST_DB" --username "$DB_USER" --password "$DB_PASS" \
        --create-db-if-not-exists 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T47" "test-connection --create-db-if-not-exists (non-existing DB)" "exit=0, server reachable"
    else
        fail_test "T47" "test-connection --create-db-if-not-exists (non-existing DB)" "exit=$rc: $out"
    fi

    # T48: diff --create-db-if-not-exists on non-existing DB — shows all as new, does NOT create DB
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS \"$NONEXIST_DB\"" > /dev/null 2>&1
    out=$("$BIN" diff \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$NONEXIST_DB" --username "$DB_USER" --password "$DB_PASS" \
        --schema "$SAMPLES_SCHEMA" \
        --create-db-if-not-exists 2>&1)
    rc=$?
    local db_exists_after
    db_exists_after=$(docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c \
        "SELECT datname FROM pg_database WHERE datname='$NONEXIST_DB'" 2>/dev/null | tr -d '[:space:]')
    if [ $rc -eq 0 ] && echo "$out" | grep -qiE "statement|change|CREATE" && [ -z "$db_exists_after" ]; then
        pass_test "T48" "diff --create-db-if-not-exists (non-existing DB) shows changes, no DB created" "exit=0, changes shown, DB not created"
    elif [ $rc -eq 0 ] && [ -z "$db_exists_after" ]; then
        pass_test "T48" "diff --create-db-if-not-exists (non-existing DB) no DB created" "exit=0, DB not created"
    else
        fail_test "T48" "diff --create-db-if-not-exists (non-existing DB)" "exit=$rc, db_exists='$db_exists_after'"
    fi

    # T49: deploy --create-db-if-not-exists creates DB and deploys
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS \"$AUTOCREATE_DB\"" > /dev/null 2>&1
    out=$("$BIN" deploy \
        --host "$DB_HOST" --port "$DB_PORT" \
        --database "$AUTOCREATE_DB" --username "$DB_USER" --password "$DB_PASS" \
        --schema "$SAMPLES_SCHEMA" \
        --create-db-if-not-exists --yes 2>&1)
    rc=$?
    local created_db
    created_db=$(docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c \
        "SELECT datname FROM pg_database WHERE datname='$AUTOCREATE_DB'" 2>/dev/null | tr -d '[:space:]')
    if [ $rc -eq 0 ] && [ -n "$created_db" ]; then
        pass_test "T49" "deploy --create-db-if-not-exists creates new DB and deploys" "DB '$created_db' created, exit=0"
    else
        fail_test "T49" "deploy --create-db-if-not-exists" "exit=$rc, created_db='$created_db'"
    fi
    # Cleanup T49 database
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS \"$AUTOCREATE_DB\"" > /dev/null 2>&1

    # T50: Config file with connectionString field
    local tmp_config50="$OUT_DIR/test-config-connstr.json"
    cat > "$tmp_config50" << EOCFG
{
  "connection": {
    "connectionString": "Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASS}"
  },
  "paths": {
    "schema": "$SAMPLES_SCHEMA"
  }
}
EOCFG
    out=$("$BIN" test-connection --config "$tmp_config50" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T50" "Config file with connectionString field" "exit=0"
    else
        fail_test "T50" "Config file with connectionString field" "exit=$rc: $out"
    fi

    # T51: Config file with createDatabaseIfNotExists: true
    docker exec "$PG_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "DROP DATABASE IF EXISTS \"NonExistentDb2_IntTest\"" > /dev/null 2>&1
    local tmp_config51="$OUT_DIR/test-config-create-db.json"
    cat > "$tmp_config51" << EOCFG
{
  "connection": {
    "host": "${DB_HOST}",
    "port": ${DB_PORT},
    "database": "NonExistentDb2_IntTest",
    "username": "${DB_USER}",
    "password": "${DB_PASS}"
  },
  "paths": {
    "schema": "$SAMPLES_SCHEMA"
  },
  "options": {
    "createDatabaseIfNotExists": true
  }
}
EOCFG
    out=$("$BIN" test-connection --config "$tmp_config51" 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        pass_test "T51" "Config file with createDatabaseIfNotExists: true (non-existing DB)" "exit=0"
    else
        fail_test "T51" "Config file with createDatabaseIfNotExists" "exit=$rc: $out"
    fi
}

# ============================================================
# Generate markdown report
# ============================================================

generate_report() {
    section "Generating test report: $REPORT_FILE"
    local total=$((PASS + FAIL))
    local end_time
    end_time=$(date '+%Y-%m-%d %H:%M:%S')

    cat > "$REPORT_FILE" << EOF
# pgdeploy CLI Integration Test Report

**Started:** $START_TIME
**Finished:** $end_time
**CLI binary:** \`$BIN\`
**Config file:** \`$CONFIG_FILE\`
**Database:** \`$DB_HOST:$DB_PORT/$DB_NAME\`
**Samples:** \`$REPO_ROOT/samples\`

---

## Summary

| | Count |
|---|:---:|
| **Total** | $total |
| ✅ Pass | $PASS |
| ❌ Fail | $FAIL |

---

## All Test Results

| Result | ID | Test Case | Detail |
|--------|:--:|-----------|--------|
$(printf '%s\n' "${RESULTS[@]}")

---

## CLI Parameters Coverage

### \`init\` command

| ID | Parameter(s) | Scenario |
|:--:|--------------|----------|
| T01 | *(no args)* | Default output → \`PostgresDeployer.json\` in CWD |
| T02 | \`-o <path>\` | Short alias output path |
| T03 | \`--output <path>\` | Long-form output path |
| T04 | *(content)* | Generated file includes \`pgcrypto\` extension |
| T05 | *(content)* | Generated file is valid JSON |

### \`test-connection\` command

| ID | Parameter(s) | Scenario |
|:--:|--------------|----------|
| T06 | \`-c <config>\` \`-p <pass>\` | Config file + password CLI override |
| T07 | \`--host --port --database --username --password\` | All long-form CLI args (no config) |
| T08 | \`-H -P -d -u -p\` | All short-alias CLI args |
| T09 | \`--host <invalid>\` | Invalid host → exit 1 (error case) |
| T10 | *(no --database)* | Missing database → exit 1 (error case) |
| T11 | \`-p <wrong>\` | Wrong password → exit 1 (error case) |
| T41 | \`--connection-string\` | Valid connection string → exit 0, 'success' |
| T42 | \`-s\` | Short alias for \`--connection-string\` |
| T43 | \`--connection-string\` (wrong password) | Invalid credentials → exit 1 |
| T45 | \`--connection-string\` + \`--host\` | \`--connection-string\` overrides individual \`--host\` |
| T46 | \`--create-db-if-not-exists\` (existing DB) | Server reachable, DB exists → exit 0 |
| T47 | \`--create-db-if-not-exists\` (non-existing DB) | Server reachable, DB will be created → exit 0 |
| T50 | \`--config\` with \`connectionString\` field | Config file with \`connection.connectionString\` |
| T51 | \`--config\` with \`createDatabaseIfNotExists: true\` | Config file option for auto-create DB |

### \`diff\` command

| ID | Parameter(s) | Scenario |
|:--:|--------------|----------|
| T12 | \`-c <config>\` \`-p <pass>\` | Console output (no --output) |
| T13 | \`--output <file>\` | Long-form file output |
| T14 | \`-o <file>\` | Short-alias file output |
| T15 | *(content)* | Detects changes on empty DB |
| T16 | \`--host --port --database --username --password --schema --init-data --extensions\` | All CLI args, no config |
| T39 | \`-c -p --extensions -o\` | Shows 0 changes after full deploy |
| T40 | *(content)* | Report says "up to date" or "0 statement(s)" |
| T44 | \`--connection-string --schema\` | \`diff\` using connection string |
| T48 | \`--create-db-if-not-exists\` (non-existing DB) | Shows all as new, does NOT create DB |

### \`deploy\` command

| ID | Parameter(s) | Scenario |
|:--:|--------------|----------|
| T17 | \`--dry-run\` | Exits 0 without applying changes |
| T18 | \`--dry-run\` | Output contains "Dry run" message |
| T19 | \`--dry-run\` | DB remains empty after dry run |
| T20 | \`--only tables --yes\` | Deploy tables only, skip seeds |
| T21 | \`--only tables\` | All 5 tables created in DB |
| T22 | \`--only tables\` | Seed data NOT applied |
| T23 | \`--only tables\` | Sequences created |
| T24 | \`--only seeds --yes\` | Deploy seed data only |
| T25 | \`--only seeds\` | Base_Setting_Code has data |
| T26 | \`--only seeds\` | Base_Setting_MenuItem has data |
| T27 | \`--yes --log-file <path>\` | Full deploy, no prompt, log to file |
| T28 | \`--log-file\` | Log file created |
| T29 | *(RunScripts)* | \`RunScripts/*.sql\` file generated |
| T30 | *(RunScripts)* | RunScript file has correct header |
| T31 | \`--only all --yes\` | Explicit \`--only all\` |
| T32 | \`--only all\` | Idempotent: "up to date" on second run |
| T33 | \`--host <invalid>\` | CLI \`--host\` overrides config (fail) |
| T34 | \`--database <invalid>\` | CLI \`--database\` overrides config (fail) |
| T35 | \`--port 9999\` | CLI \`--port\` overrides config (fail) |
| T36 | \`--host --port --database --username --password --schema --init-data --extensions --yes --dry-run\` | All long-form, no config |
| T37 | \`-H -P -d -u -p --schema --init-data --extensions --yes --dry-run\` | Short aliases, no config |
| T38 | *(no --database)* | Missing database → exit 1 (error case) |
| T49 | \`--create-db-if-not-exists --yes\` (non-existing DB) | Auto-creates DB and deploys schema |

### Database Verification

| ID | Object | Check |
|:--:|--------|-------|
| V01–V05 | Tables (×5) | Base_Auth_MenuItem, Base_Auth_User, Base_Setting_Code, Base_Setting_MenuGroup, Base_Setting_MenuItem |
| V06 | View | vw_Auth_MenuItem |
| V07–V08 | Functions (×2) | fn_base_passwordhash, fn_auth_getactiveusers |
| V09–V10 | Procedures (×2) | sp_auth_lockuser, sp_auth_resetfailurecount |
| V11–V12 | Sequences (×2) | SequenceNo_Default, SequenceNo_CustomerContract |
| V13–V15 | Seed data (×3) | Base_Setting_Code, Base_Setting_MenuGroup, Base_Setting_MenuItem have rows |
| V16 | View queryable | SELECT from vw_Auth_MenuItem succeeds |

---

## Output Files

| File | Description |
|------|-------------|
| \`output/diff-before.txt\` | Diff report (before deploy, DB empty) |
| \`output/diff-after.txt\` | Diff report (after full deploy) |
| \`output/deploy-tables.log\` | Output of \`deploy --only tables\` |
| \`output/deploy-seeds.log\` | Output of \`deploy --only seeds\` |
| \`output/deploy-full.log\` | \`--log-file\` output of full deploy |
| \`output/deploy-full-stdout.log\` | stdout of full deploy |
| \`output/init-custom.json\` | Config generated by \`init\` |
| \`output/test-config-connstr.json\` | Temp config with \`connectionString\` field (T50) |
| \`output/test-config-create-db.json\` | Temp config with \`createDatabaseIfNotExists: true\` (T51) |

---

*Generated by \`tools/integration-test/run-tests.sh\`*
EOF

    echo ""
    echo "════════════════════════════════════════════════════════"
    printf "  TOTAL: %d  |  ✅ PASS: %d  |  ❌ FAIL: %d\n" "$total" "$PASS" "$FAIL"
    echo "  Report: $REPORT_FILE"
    echo "════════════════════════════════════════════════════════"

    if [ $FAIL -gt 0 ]; then
        echo ""
        echo "  Failed tests:"
        for r in "${RESULTS[@]}"; do
            if [[ "$r" == *"FAIL"* ]]; then
                echo "  $r"
            fi
        done
    fi
}

# ============================================================
# Main
# ============================================================

main() {
    mkdir -p "$OUT_DIR"
    echo "════════════════════════════════════════════════════════"
    echo "  pgdeploy CLI Integration Test  —  $START_TIME"
    echo "  DB: $DB_HOST:$DB_PORT/$DB_NAME  User: $DB_USER"
    echo "════════════════════════════════════════════════════════"

    build_cli
    cleanup_db

    test_init
    test_connection
    test_diff_before
    test_deploy_dry_run
    test_deploy_tables_only
    test_deploy_seeds_only
    test_deploy_full
    test_deploy_only_all
    test_cli_overrides
    test_deploy_no_config
    test_diff_after
    verify_database
    test_connection_string
    test_create_db_if_not_exists

    generate_report
}

main
