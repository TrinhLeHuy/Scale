\copy (select * from trips where id in ('e0202f8c-e6a3-4984-9c6e-154b70e9b658','c611977b-0357-4ec4-8df7-bed1a50b414c')) to '/tmp/trips-cleanup-20261103.csv' csv header
begin;
update qr_tokens set status='Revoked', revoked_at=now(), updated_at=now(), updated_by='admin-cleanup', version=version+1
 where trip_id in ('e0202f8c-e6a3-4984-9c6e-154b70e9b658','c611977b-0357-4ec4-8df7-bed1a50b414c') and status='Issued';
insert into audit_log_entries(id, action_type, trip_id, actor, before_json, after_json, reason, correlation_id, occurred_at, created_at, created_by, version)
select gen_random_uuid(), 'TRIP_DELETED', id, 'admin-cleanup',
  jsonb_build_object('lifecycle', lifecycle, 'tripNo', trip_no, 'reference', reference_trip_no),
  jsonb_build_object('deletedAt', now()),
  'Dọn dữ liệu: phiếu đã xóa trên FRM', replace(gen_random_uuid()::text,'-',''), now(), now(), 'admin-cleanup', 1
from trips where id in ('e0202f8c-e6a3-4984-9c6e-154b70e9b658','c611977b-0357-4ec4-8df7-bed1a50b414c');
update trips set lifecycle='Cancelled', assigned_weighbridge_id=null, queue_sequence_number=null,
  deleted_at=now(), updated_at=now(), updated_by='admin-cleanup', version=version+1
 where id in ('e0202f8c-e6a3-4984-9c6e-154b70e9b658','c611977b-0357-4ec4-8df7-bed1a50b414c') and deleted_at is null;
commit;
select trip_no, lifecycle, deleted_at is not null from trips where id in ('e0202f8c-e6a3-4984-9c6e-154b70e9b658','c611977b-0357-4ec4-8df7-bed1a50b414c');
