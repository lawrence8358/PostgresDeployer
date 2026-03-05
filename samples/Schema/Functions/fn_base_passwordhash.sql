-- PostgreSQL 版本：fn_base_passwordhash
-- 依賴：pgcrypto extension（提供 digest 函式）
-- 對應 MSSQL: dbo.fn_Base_PasswordHash
-- 輸出格式：SHA-512 雜湊值的大寫十六進位字串，前綴 0x（與 MSSQL CONVERT(..., 1) 一致）
--
-- 重要：MSSQL HASHBYTES('SHA2_512', nvarchar_value) 在雜湊前會將字串以 UTF-16LE 編碼，
-- 而 PostgreSQL digest() 預設使用 UTF-8。對於 ASCII 輸入（GUID + 密碼 + Salt），
-- UTF-16LE 的每個字元由 [char_byte, 0x00] 兩個位元組組成。
-- 此函式透過對每個 ASCII 字元插補 0x00 來模擬 MSSQL 行為，確保兩平台密碼雜湊相容。

CREATE OR REPLACE FUNCTION fn_base_passwordhash(
    p_id VARCHAR(50),
    p_password VARCHAR(50)
)
RETURNS VARCHAR(200)
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    combined      TEXT;
    utf16le_hex   TEXT;
    utf16le_bytes BYTEA;
BEGIN
    combined := p_id || p_password || 'HelloSalt';

    -- 模擬 MSSQL HASHBYTES 對 nvarchar 的 UTF-16LE 編碼行為：
    -- 對於 ASCII 輸入（GUID、一般密碼、'HelloSalt'），每個字元編碼為 [ASCII_byte, 0x00]
    SELECT string_agg(
        encode(convert_to(substring(combined, gs, 1), 'UTF8'), 'hex') || '00',
        '' ORDER BY gs
    )
    INTO utf16le_hex
    FROM generate_series(1, length(combined)) gs;

    utf16le_bytes := decode(utf16le_hex, 'hex');

    RETURN '0x' || UPPER(ENCODE(digest(utf16le_bytes, 'sha512'), 'hex'));
END;
$$;
