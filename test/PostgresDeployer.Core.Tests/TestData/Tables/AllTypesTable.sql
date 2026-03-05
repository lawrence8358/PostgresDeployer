CREATE TABLE "Test_AllTypes"
(
    "ColUuid" UUID NOT NULL,
    "ColVarchar" VARCHAR(200) NULL,
    "ColChar" NCHAR(10) NULL,
    "ColText" TEXT NULL,
    "ColSmallint" SMALLINT NOT NULL DEFAULT 0,
    "ColInt" INT NOT NULL,
    "ColBigint" BIGINT NULL,
    "ColBoolean" BOOLEAN NOT NULL DEFAULT FALSE,
    "ColDecimal1" DECIMAL(18,10) NULL,
    "ColDecimal2" DECIMAL(8) NULL,
    "ColDecimal3" DECIMAL NULL,
    "ColTimestamptz" TIMESTAMPTZ NULL DEFAULT NOW(),
    "ColTimestamp" TIMESTAMP NULL,
    "ColDate" DATE NULL,
    "ColBytea" BYTEA NULL,
    "ColIdentity" INT GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Test_AllTypes" PRIMARY KEY ("ColUuid")
);
