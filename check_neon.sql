\c postgresql://neondb_owner:npg_u4Ao1JEqpysY@ep-solitary-field-a1ai56u3-pooler.ap-southeast-1.aws.neon.tech:5432/neondb?sslmode=prefer
\dt
SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';
SELECT table_name FROM information_schema.tables WHERE table_name ILIKE '%order%';
\q