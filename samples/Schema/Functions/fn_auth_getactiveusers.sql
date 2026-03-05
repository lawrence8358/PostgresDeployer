CREATE OR REPLACE FUNCTION fn_auth_getactiveusers(
    p_type SMALLINT DEFAULT NULL
)
RETURNS TABLE (
    "Id"              UUID,
    "Account"         VARCHAR(200),
    "Name"            VARCHAR(500),
    "Email"           VARCHAR(500),
    "Type"            SMALLINT,
    "LastSigninDate"  TIMESTAMPTZ
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        u."Id",
        u."Account",
        u."Name",
        u."Email",
        u."Type",
        u."LastSigninDate"
    FROM "Base_Auth_User" u
    WHERE u."Active" = TRUE
      AND (p_type IS NULL OR u."Type" = p_type)
    ORDER BY u."Cix";
END;
$$;
