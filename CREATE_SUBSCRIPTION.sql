-- 创建从neon到本地的逻辑复制订阅
-- 这个脚本需要连接到本地PostgreSQL数据库执行

CREATE SUBSCRIPTION neon_to_local_subscription
CONNECTION 'host=ep-solitary-field-a1ai56u3-pooler.ap-southeast-1.aws.neon.tech port=5432 dbname=neondb user=neondb_owner password=npg_u4Ao1JEqpysY sslmode=prefer'
PUBLICATION cdc_publication
WITH (slot_name = neon_to_local_slot);