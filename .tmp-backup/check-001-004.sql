\pset pager off
select occurred_at, operation, http_status, success, substring(request_body from position('LanCan' in request_body) for 160)
from api_call_logs
where operation = 'new_phieucan' and (request_body like '%20261028-001%' or request_body like '%LD2526-00014689%')
order by occurred_at;
select s.segment_index, s.role, s.status, s.opened_at, s.closed_at from weigh_sessions s
join trips t on t.id = s.trip_id where t.trip_no = '20261028-001' order by s.opened_at;
