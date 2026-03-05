CREATE TABLE "Test_Simple"
(
    "Id" UUID NOT NULL,
    "Name" VARCHAR(200) NULL,
    "Cix" INT GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Test_Simple" PRIMARY KEY ("Id")
);

CREATE INDEX "CIX_Test_Simple" ON "Test_Simple" ("Cix");
