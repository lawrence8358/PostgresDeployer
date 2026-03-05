CREATE OR REPLACE PROCEDURE sp_auth_resetfailurecount(
    IN  p_id        UUID,
    OUT p_success   BOOLEAN
)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE "Base_Auth_User"
    SET
        "FailureCount" = 0,
        "LockDate"     = NULL,
        "ModifiedDate" = NOW()
    WHERE "Id" = p_id;

    -- FOUND 為 TRUE 表示 UPDATE 影響了至少一筆資料
    p_success := FOUND;
END;
$$;
