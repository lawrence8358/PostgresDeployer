CREATE TABLE IF NOT EXISTS "Test_IfNotExists"
(
    "Id" UUID NOT NULL,
    "Name" VARCHAR(200) NULL,
    CONSTRAINT "PK_Test_IfNotExists" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_Test_IfNotExists_Name" ON "Test_IfNotExists" ("Name");
