CREATE TABLE "Test_Complex"
(
    "Id" UUID NOT NULL,
    "Account" VARCHAR(200) NOT NULL,
    "Name" VARCHAR(500) NULL,
    "Active" BOOLEAN NOT NULL DEFAULT FALSE,
    "Score" DECIMAL(18,10) NULL,
    "Rating" DECIMAL(8) NULL DEFAULT 0,
    "Amount" DECIMAL NULL,
    "Bio" TEXT NULL,
    "Photo" BYTEA NULL,
    "Type" SMALLINT NOT NULL,
    "BigCount" BIGINT NULL DEFAULT 0,
    "CreatedDate" TIMESTAMPTZ NULL DEFAULT NOW(),
    "BirthDate" DATE NULL,
    "Code" NCHAR(10) NULL,
    "Cix" BIGINT GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Test_Complex" PRIMARY KEY ("Id")
);

CREATE INDEX "CIX_Test_Complex" ON "Test_Complex" ("Cix" DESC);
CREATE UNIQUE INDEX "IX_Test_Complex_Account" ON "Test_Complex" ("Account", "Type");
