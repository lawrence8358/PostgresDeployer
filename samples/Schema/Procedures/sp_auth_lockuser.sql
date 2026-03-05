CREATE OR REPLACE PROCEDURE sp_auth_lockuser(
    p_id            UUID,
    p_locked_by     UUID DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE "Base_Auth_User"
    SET
        "Active"       = FALSE,
        "LockDate"     = NOW(),
        "ModifiedBy"   = p_locked_by,
        "ModifiedDate" = NOW()
    WHERE "Id" = p_id;
END;
$$;
