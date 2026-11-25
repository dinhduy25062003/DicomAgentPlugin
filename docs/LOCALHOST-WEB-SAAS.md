# Luồng web cùng máy → app local → SaaS (0.2.12)

Tài liệu này mô tả runtime hiện tại và thay thế đề xuất đăng nhập/SignalR trong `THIET-KE-TICH-HOP-TENANT.md`. Không cần đăng nhập riêng trong app. Browser trên **cùng máy** gọi API loopback khi dịch vụ Host đang chạy; backend SaaS không gọi localhost. App chỉ nhận ảnh từ máy chụp, không điều khiển việc phát tia/chụp.

## Sử dụng

1. Cài bản 0.2.12, mở Desktop. Dịch vụ `DicomAgent` (Windows) / `dicom-agent.service` (Linux) tự chạy sau cài đặt và khởi động máy; API mặc định `http://127.0.0.1:17890`. Đóng Desktop xuống tray hoặc thoát Desktop không dừng Host/API.
2. Vào **Cấu hình kết nối**: đặt AE Title, IP listen của máy Agent, port DICOM, IP/Calling AE của máy chụp, thư mục lưu ảnh. Bấm **Lưu cấu hình** rồi xác thực quản trị nếu được yêu cầu; app tự cấp quyền thư mục, restart service và kết nối lại. Không cần chạy lệnh. Trên console máy chụp, đặt đích là IP LAN của Agent + port DICOM + AE Title. IP listen không phải IP của máy chụp.
3. Web gửi request. Request hợp lệ đầu tiên sẽ gắn installation vào một `TenantName` cố định (một nha khoa/chi nhánh); app không hỏi lại tenant. Request khác tenant bị HTTP 409, kể cả đã xóa hết request hoặc restart app. Tenant đang gắn hiển thị trên UI. Không cung cấp nút đổi tenant để tránh chuyển nhầm dữ liệu.
4. **Quản lý request**: tìm theo tên đối tượng, model, khách, mã khách, dịch vụ, resId; lọc trạng thái; bấm tên để xem detail; **Thực hiện** để mở màn hình dịch vụ và bật nhận ảnh. **Xóa request** cần xác nhận và chỉ xóa dữ liệu request; file DICOM gốc vẫn nằm trong kho local.
5. **Thực hiện dịch vụ** hiển thị đủ thông tin request, danh sách ảnh/PatientName/PatientID/Study, ảnh xem trước frame đầu tiên. Có thể chọn nhiều file (kể cả DICOM không có extension qua bộ lọc All) hoặc chọn thư mục chứa `.dcm`/`.dicom` (tìm cả thư mục con, tối đa 100 ảnh/ca). Desktop truyền bytes qua HTTP localhost tới service, nên service không cần quyền đọc thư mục cá nhân của người dùng.
6. Mỗi lúc chỉ một request nhận DICOM. Dừng ca hiện tại trước khi bắt đầu ca khác. Phiên nhận được cố định từ lúc thiết bị mở DICOM association; association cũ sau chuyển/dừng ca không tự gán sang ca mới. Ảnh chưa có ca hoặc đến muộn vẫn lưu trong kho DICOM và có thể chọn lại bằng file picker.
7. Đối chiếu đúng khách/ca và tất cả ảnh, đánh dấu xác nhận, bấm **Done — Gửi lên SaaS**. Chỉ các ảnh còn trong danh sách được gửi. App đối chiếu danh sách ảnh đã xác nhận; nếu có ảnh mới đến trước lúc Done, yêu cầu kiểm tra lại. Request chuyển `uploading`, rồi `done` sau HTTP thành công từ server. Ca `done` không nhận thêm ảnh và không gửi lại bằng nút Done.

Ảnh xem trước chỉ phục vụ kiểm tra file/frame đầu tiên; chưa có công cụ chẩn đoán, chọn mọi frame, window/level hoặc codec mở rộng cho mọi transfer syntax. File không xem trước được vẫn có thể upload nguyên bản. Bộ nhận mạng giữ các transfer syntax uncompressed đã hỗ trợ trước đó; không tự chuyển JPEG/PNG.

## API local

Chỉ bind IPv4 loopback `127.0.0.1`, không mở firewall cho port API, không truy cập qua IP LAN. Dùng URL có `127.0.0.1` để tránh `localhost` resolve sang IPv6. Host header cũng được kiểm tra để chặn DNS rebinding.

- `GET /api/health` → `{ "application": "DicomAgent", "status": "ready" }`.
- `POST /api/capture-requests` → HTTP 201, trả request gồm `id`, thông tin, thời gian và trạng thái.
- `GET /api/capture-requests/{id}` → trạng thái cùng request; phải gửi đúng Authorization đang lưu và TenantName. Không trả token, đường dẫn file local hay PatientID/PatientName đọc từ DICOM.
- `OPTIONS` hỗ trợ CORS. Origin production được phép tự động theo URL SaaS của tenant. Origin dev phải thêm vào cấu hình; không dùng wildcard. Endpoint tạo ca kiểm tra Origin khớp tenant nếu Origin không nằm trong danh sách bổ sung.

Header tạo ca:

```http
Authorization: Bearer <access-token-cua-web>
TenantName: demo1
Content-Type: application/json
Idempotency-Key: <UUID-cua-lan-chup>
```

```json
{
  "resModel": "crm.service",
  "resId": "11111111-1111-4111-8111-111111111111",
  "EntityName": "Phiếu chụp X-quang 001",
  "ModelName": "Điều trị",
  "serviceName": "Chụp Panorama",
  "PartnerId": "123",
  "PartnerName": "Khách thử nghiệm"
}
```

Tên field không phân biệt hoa/thường. `resId` là Guid theo DTO IrAttachments hiện có; `PartnerId` chấp nhận string hoặc số nguyên, lưu dạng string. Các trường đều bắt buộc, tối đa 512 ký tự mỗi trường. Body tối đa 64 KiB. Authorization yêu cầu Bearer; app chuyển tiếp token sang SaaS, không tự cấp token hoặc thay thế kiểm tra quyền tenant/resModel/resId tại SaaS. Việc gắn tenant đầu tiên phải thực hiện từ web nha khoa tin cậy trên máy được quản lý; app không có khóa public để tự kiểm chứng chữ ký JWT của SaaS.

`Idempotency-Key` không bắt buộc để tương thích payload yêu cầu, nhưng **web nên luôn gửi** UUID cố định cho một lần chụp. Gửi lại cùng key và cùng body/tenant cập nhật token mà không tạo trùng, không reset ảnh/trạng thái; cùng key nhưng khác thông tin trả 409. Không gửi key thì app sinh UUID, nhưng vẫn từ chối nếu bộ resModel + resId + PartnerId đã có request trạng thái new trong storage. Giữ key khi retry lỗi mạng hoặc gia hạn token. Lần chụp mới được tạo khi không có request new trùng bộ ba này; request cũ đã done không chặn tạo ca mới. Khi xóa request, key đó không còn được giữ để chống giao lại.

Mã lỗi: 400 input sai; 401 thiếu/sai token khi đọc trạng thái; 403 Origin/Host không hợp lệ; 404 không có request; 409 khác tenant/key trùng khác thông tin hoặc trùng request new theo resModel + resId + PartnerId; 429 vượt giới hạn 120 request/phút; 503 kho request không truy cập được. Chỉ desktop/operator qua API quản trị HTTP có khóa local mới có quyền thay đổi trạng thái, gán ảnh, xóa request hoặc upload; web không có endpoint đọc danh sách toàn bộ ca hay đọc file local.

## Mẫu gọi từ browser

Tạo `captureId` một lần cho thao tác giao ca, giữ lại để retry và theo dõi trạng thái; không sinh UUID mới trong mỗi lần retry.

```javascript
const captureId = crypto.randomUUID();
const localBase = 'http://127.0.0.1:17890';

async function sendCapture(accessToken, tenantName, body) {
  const response = await fetch(`${localBase}/api/capture-requests`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
      TenantName: tenantName,
      'Idempotency-Key': captureId,
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`Local Agent: HTTP ${response.status}`);
  return await response.json();
}

async function getCaptureStatus(accessToken, tenantName) {
  const response = await fetch(`${localBase}/api/capture-requests/${captureId}`, {
    headers: { Authorization: `Bearer ${accessToken}`, TenantName: tenantName },
  });
  if (!response.ok) throw new Error(`Local Agent: HTTP ${response.status}`);
  return await response.json();
}
```

Web nên bắt lỗi fetch và hướng dẫn kiểm tra Host đang chạy, đúng port, Origin được cho phép và quyền truy cập mạng cục bộ của browser. Web HTTPS cần cho phép Local Network/Loopback Access khi Chrome/Edge hỏi; CORS không thay thế quyền này. Nếu web có Content-Security-Policy `connect-src`, cần cho phép `http://127.0.0.1:17890`. Không dùng `mode: 'no-cors'`, không tắt bảo mật trình duyệt.

Tham khảo chính thức: [Chrome Local Network Access](https://developer.chrome.com/docs/modern-web-guidance/guides/security/local-network-access), [Edge Local Network Access](https://learn.microsoft.com/en-us/deployedge/ms-edge-local-network-access). Kiểm tra preflight ở mức HTTP đã được tự động hóa; quyền browser với web HTTPS thật vẫn cần kiểm thử trên máy Windows triển khai.

## Upload IrAttachments

URL mặc định: `POST https://{TenantName}.tdental.vn/api/IrAttachments`. Không mang `/#/` vào API URL. Server URL được chụp lại theo từng request khi tiếp nhận; thay cấu hình không chuyển các ca cũ sang host khác. TenantName chỉ cho phép một DNS label, không chấp nhận URL/path. SaaS URL phải HTTPS (HTTP loopback chỉ để kiểm thử). HTTP redirect không được tự đi theo để tránh chuyển Authorization sang origin khác.

Header upload: chỉ thêm `Authorization` của đúng ca. Tenant được gửi trong multipart `Attachments.Tenant`; không gửi header `TenantName` hoặc `Idempotency-Key`.

Multipart:

| Part | Giá trị |
| --- | --- |
| `ResId` | Guid từ `resId` |
| `ResModel` | Từ `resModel` |
| `Attachments.Tenant` | TenantName cố định của ca |
| `Attachments.Files` | Bytes file DICOM; lặp cùng tên part cho nhiều file |

Các field EntityName/ModelName/serviceName/PartnerId/PartnerName dùng cho UI/local, không thêm vào DTO upload đã có. Không gửi local path thay bytes. Không upload bên trong response C-STORE. API IrAttachments hiện trả HTTP thành công sau khi xử lý; app không tự refresh JWT vì chưa có hợp đồng endpoint refresh. Khi token hết hạn, web gửi lại **cùng ca/key/body** với Authorization mới, rồi người dùng bấm Done lại.

Lỗi xác thực/đầu vào giữ ảnh tại local, hiển thị lỗi; lỗi mạng/timeout/5xx hoặc restart lúc đang upload chuyển `review`. Người dùng kiểm tra đủ file trên SaaS rồi chọn **Đã kiểm tra: đủ ảnh** để đánh dấu done, hoặc **chưa có ảnh, cho thử lại** để trở lại new. Nếu SaaS đã nhận một phần, xử lý/đối chiếu trên SaaS trước khi cho thử lại; app không biết file IDs vì API hiện không trả receipt từng file. Không tự retry mù. App không thể bảo đảm exactly-once khi server đã ghi ảnh nhưng mất response; giữ luồng review và xác nhận trước khi gửi lại.

## Dữ liệu, cấu hình, phát triển

- Request: `%ProgramData%\DicomAgent\requests\<id>.json` hoặc `/var/lib/dicom-agent/requests/<id>.json`. Ghi file tạm, flush, atomic rename; tồn tại sau restart. Chọn ca active không tự khôi phục sau restart để tránh nhận nhầm khách.
- `requests/tenant-binding` ghi tenant cố định, không bị xóa khi xóa các request. Đổi tenant là thao tác quản trị/migration dữ liệu có chủ đích, không tự reset bằng cách cài đè app.
- Authorization không gửi về UI/web, không ghi log. Trong JSON được mã hóa: Windows DPAPI LocalMachine + ACL installer; Linux AES-GCM với khóa `requests/.key` mode 0600, request mode 0600, directory mode 0700. Sao lưu phải giữ đầy đủ requests/key và hạn chế quyền; DPAPI gắn với máy Windows. Không sửa file JSON bằng tay khi Host đang chạy.
- File DICOM và lịch sử SQLite cũ được giữ. Xóa request/bỏ ảnh khỏi ca không xóa file gốc. Kho ảnh và metadata có thông tin bệnh nhân; group operator là quyền quản trị cục bộ.
- Port và URL template lưu trong `settings.json` phần `LocalApi`, được nhập qua màn hình cấu hình. Sau lưu restart dịch vụ để áp dụng. Ví dụ:

```json
{
  "LocalApi": {
    "Port": 17890,
    "SaasBaseUrlTemplate": "https://{TenantName}.tdental.vn",
    "AllowedOrigins": ["http://localhost:4200"]
  }
}
```

Chạy thử từ source (hai terminal, cùng DICOM_AGENT_DATA):

```bash
DICOM_AGENT_DATA="$PWD/.data" dotnet run --project src/DicomAgent.Host
DICOM_AGENT_DATA="$PWD/.data" dotnet run --project src/DicomAgent.Desktop
```

Không cần đổi DB lịch sử hoặc chạy migration mới cho request JSON. Build/test: `dotnet build DicomAgent.sln -c Release` và `dotnet test DicomAgent.sln -c Release`.


## Luồng debug theo lớp (0.2.2)

Chạy/debug project `src/DicomAgent.Host`. Mở Desktop riêng để thực hiện ca; API/DICOM/upload do Host xử lý.

```text
HTTP POST /api/capture-requests
  Host/Controllers/CaptureRequestsController.Create
    Application/Services/CaptureRequestService.ReceiveAsync
      ICaptureRequestRepository → Infrastructure/Capture/JsonCaptureRequestRepository
      IRequestSecretStore → Infrastructure/Capture/RequestSecretStore

Desktop → HTTP POST /api/management/commands (capture-start)
  ManagementController → ManagementService → ICaptureRequestService.ActivateAsync

DICOM C-STORE
  StorageScp → InstanceStore → ICaptureRequestService.AttachAsync

Desktop → HTTP POST /api/management/commands (capture-done)
  ManagementController → ManagementService → ICaptureRequestService.QueueUploadAsync
    ICaptureUploadQueue → Infrastructure/Capture/CaptureUploadQueue
  Host/Workers/CaptureUploadWorker
    Application/Services/CaptureRequestService.RunUploadsAsync → ProcessUploadAsync
      IRequestSecretStore.Unprotect (token riêng của ca)
      ISaasAttachmentClient.UploadAsync → Infrastructure/Capture/SaasAttachmentClient
        HttpClient.SendAsync → SaaS /api/IrAttachments
      ICaptureRequestRepository.SaveAsync (done/new/review)
```

Đặt breakpoint lần lượt tại Controller `Create`, Application `ReceiveAsync`, repository `SaveAsync`; khi Done đặt breakpoint tại Application `ProcessUploadAsync` và Infrastructure `SaasAttachmentClient.UploadAsync`. Upload chạy qua queue/worker nên không nằm cùng call stack HTTP tạo ca. Khi request không vào Controller, kiểm tra Host/Origin middleware trong `Host/Api/LocalApiServer`.

Controller giữ nguyên endpoint/header/body/status và CORS; JSON, tenant-binding, định dạng mã hóa token tương thích bản 0.2.0. Unit test Application chỉ tham chiếu project Application, dùng các port giả lập; không chạy filesystem/HTTP/DICOM.


## Cài đặt và tự khởi động từ 0.2.2

Linux: `sudo apt install ./dicom-agent_0.2.12_amd64.deb`, sau đó mở DICOM Desktop Agent từ menu. Gói có dependency `acl`, tự thêm tài khoản gọi sudo vào nhóm operator và cấp quyền UID trực tiếp, nên không cần usermod hay đăng xuất. Host ghi desktop-connection.json chứa port HTTP và khóa local; ACL readonly cho operator được kế thừa từ thư mục data. Không đổi quyền other sang đọc/ghi toàn cục, không mở quyền thư mục request/token riêng tư. Khi cài bằng root không qua sudo/polkit, quản trị có thể chỉ định DICOM_AGENT_OPERATOR_USER.

Windows: chạy Setup 0.2.5, chọn đúng tài khoản vận hành. Bộ cài cấp SID trực tiếp trên data, cài/bật service Automatic và đợi Running. Mở UI ngay, không cần làm mới group token bằng logout.

Cả hai hệ điều hành: **Cấu hình kết nối → Mở giao diện khi đăng nhập máy → Lưu tùy chọn khởi động**. Đây là tùy chọn UI theo từng tài khoản, mặc định tắt. Windows dùng HKCU Run; Linux dùng XDG autostart tại `~/.config/autostart/dicom-agent.desktop` (hoặc XDG_CONFIG_HOME). Tắt UI autostart không dừng service nhận ảnh nền. Cài đè giữ lựa chọn của người dùng.


## Chỉ dùng HTTP localhost từ 0.2.2

- Web → `POST http://127.0.0.1:17890/api/capture-requests`, header Authorization/TenantName và body như trên. Đây là API public cho web trên cùng máy.
- Desktop → `POST /api/management/commands` và `POST /api/management/captures/{id}/files` trên cùng port HTTP. Không dùng Unix socket, named pipe hay WebSocket.
- Host tự ghi `desktop-connection.json` gồm BaseUrl/Key, mode0640 trên Linux, ACL readonly cho người cài; Windows kế thừa SID read từ data. Desktop tự đọc và gửi X-DicomAgent-Key, người dùng không phải nhập hoặc cấu hình khóa này.
- API quản trị từ chối mọi Origin của browser và yêu cầu khóa local. Public web API vẫn chỉ cần Authorization/TenantName; khóa local không thay thế token upload SaaS. Host đổi khóa local khi restart, Desktop tự đọc lại.
- Import file HTTP cho phép dung lượng theo MaxInstanceBytes, tách khỏi giới hạn 64 KiB của JSON tạo request. Các thao tác vẫn đi qua Controller → Application → Infrastructure.


## Chặn request new trùng theo đối tượng/khách từ 0.2.3

Trước khi tạo request mới, Application kiểm tra request đang ở trạng thái `new` theo bộ ba `resModel + resId + PartnerId`. Nếu có ca new trùng cả ba trường, API trả HTTP409 với lỗi `A new request with the same resModel, resId and PartnerId already exists in local storage.` Không thêm request mới hoặc thay token/nội dung ca đã có. Các trường EntityName/ModelName/serviceName/PartnerName khác nhau vẫn bị chặn nếu bộ ba không đổi.

Ca cũ đã `done` vẫn cho phép tạo một ca mới cùng bộ ba. Ca mới có UUID và token riêng, trạng thái new; từ đó các lần tạo trùng tiếp theo bị chặn đến khi ca mới không còn ở trạng thái new hoặc đã bị xóa. Dữ liệu và token ca done được giữ nguyên.

Kiểm tra nằm trong cùng khóa đồng bộ với thao tác lưu JSON, nên hai lời gọi đồng thời chỉ tạo được một ca new. Có hiệu lực sau restart và khi web không gửi Idempotency-Key. Cùng Idempotency-Key và cùng body vẫn được gửi lại để gia hạn token, giữ ảnh và trạng thái; thao tác này không tạo ca mới. Các trạng thái khác new không tham gia điều kiện chặn.

Từ 0.2.8, **Cấu hình kết nối → Giao diện Desktop → Chủ đề màu** cho chọn **Sáng** hoặc **Tối**, áp dụng ngay và ghi nhớ cho tài khoản hiện tại trên cả Windows/Linux. Tùy chọn giao diện lưu riêng, không cần lưu cấu hình Host hoặc restart dịch vụ.

Lưu tự động 0.2.8 giữ nguyên API management `save` cũ (lưu, cần restart); chỉ luồng nút Lưu trên Desktop chuyển sang Host helper `--apply-settings` có xác thực quản trị. Cấu hình Receiver vẫn là snapshot từng tiến trình. Helper không dùng token SaaS, không thay tenant/request, chặn áp dụng khi có upload đang chạy và rollback settings/sandbox nếu service không khởi động được.

Hợp đồng upload đã xác nhận: POST IrAttachments chỉ thêm header Authorization của request; không thêm header TenantName hay Idempotency-Key. Multipart vẫn gồm ResId, ResModel, Attachments.Tenant và Attachments.Files. Header TenantName/Idempotency-Key từ web xuống API local vẫn giữ nguyên.
