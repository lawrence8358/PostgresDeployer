CREATE TABLE "Base_Auth_MenuItem"
(
    "RoleCode" VARCHAR(50) NOT NULL, 
    "ItemCode" VARCHAR(50) NOT NULL, 
    CONSTRAINT "PK_Base_Auth_MenuItem" PRIMARY KEY ("RoleCode", "ItemCode")
)