CREATE TABLE "Test_CompoundPk"
(
    "RoleCode" VARCHAR(50) NOT NULL,
    "UserId" UUID NOT NULL,
    "Active" BOOLEAN NOT NULL DEFAULT TRUE,
    "Cix" INT GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Test_CompoundPk" PRIMARY KEY ("RoleCode", "UserId")
);

CREATE INDEX "CIX_Test_CompoundPk" ON "Test_CompoundPk" ("Cix");
