# Thiết kế app chụp ảnh tích hợp tenant

Trạng thái: đã đối chiếu 5 bước yêu cầu ngày 10/11/2026, chưa triển khai runtime. Đã có method/path/body và response login thành công, DTO multipart upload chính xác; còn thiếu hợp đồng giao ca, auth scheme/refresh và response upload. API giao ca qua server trung gian bên dưới là đề xuất, chưa xác nhận server đã hỗ trợ.

## 1. Yêu cầu đã chốt

- App cài tại phòng khám, nhận ảnh DICOM do máy chụp/console gửi về.
- Người dùng app nhập tên miền tenant/nha khoa, tài khoản và mật khẩu để gọi API login và nhận token.
- Web giao yêu cầu gồm tên khách (`CustomerName`), loại đối tượng (`ResModel`) và ID đối tượng (`ResId`). Đề xuất kèm `RequestId`, `AgentId`; `CustomerId` là trường bổ sung nếu web có, không phải yêu cầu bắt buộc đã được chốt.
- Kỹ thuật viên chọn yêu cầu/khách/đối tượng, bắt đầu ca, kiểm tra các ảnh nhận được và xác nhận.
- Xác nhận trên app là upload và gắn file ngay vào đối tượng qua API tenant nhận multipart/IFormFile. Không còn bước chờ người dùng bấm Lưu trên web.
- Web có thể ở ngoài phòng khám. Giao yêu cầu và nhận trạng thái qua server trung gian, không phụ thuộc browser cùng máy hoặc cùng LAN với app.
- Giữ Host nhận ảnh chạy nền; UI Windows tiếp tục hỗ trợ tray. File gốc giữ cục bộ khi lỗi mạng/upload.

## 2. Đường kết nối

```text
Web ── tạo yêu cầu/xem kết quả ──> API tenant / server trung gian
                                          ^
                                          |
                              kết nối đi ra do app mở
                              nhận ca / báo trạng thái / upload
                                          |
Máy chụp ── DICOM C-STORE ──> Host + Desktop tại phòng khám
```

Tên miền người dùng nhập là địa chỉ server app gọi ra, không tự tạo đường truy cập từ Internet vào máy cài app. Server trung gian cung cấp API web gọi; app nhận yêu cầu từ kênh kết nối chủ động tới server đó. Không publish DICOM port hoặc API quản trị nội bộ ra Internet.

Đề xuất SignalR trên server tenant/trung gian: app chủ động kết nối HTTPS có xác thực và nhận event giao ca; web gọi API server thay vì port máy cài app. Nếu chưa dùng SignalR, có thể triển khai polling có cursor/ack. Dù dùng push, server vẫn cần lưu yêu cầu và cho app lấy lại sau mất kết nối; reconnect không thay cho hàng đợi bền vững. URL/event thực tế chờ hợp đồng backend.

Web tạo ca nhận mã yêu cầu; không giữ một HTTP request chờ suốt quá trình chụp. Web theo dõi trạng thái và lấy danh sách file đã gắn từ server sau khi upload thành công.

Nếu backend hiện chỉ có login và upload, cần bổ sung phần giao ca/định tuyến thiết bị/trạng thái ở server. Chỉ thay app không đủ để web ở ngoài phòng khám giao việc qua server trung gian.

## 3. Dữ liệu ca chụp đề xuất

```json
{
  "RequestId": "ma-ca-duy-nhat",
  "AgentId": "may-nhan-anh-duoc-chi-dinh",
  "CustomerName": "Nguyen Van A",
  "ResModel": "gia-tri-model-theo-backend",
  "ResId": "11111111-1111-4111-8111-111111111111"
}
```

ResId phải parse được thành Guid theo CreateIrAttachmentsCommand; không dùng mã phiếu hiển thị thay cho ID. Tên field tạo ca là đề xuất; `ResModel` và `ResId` giữ ý nghĩa theo API đích. Tên khách chỉ phục vụ hiển thị, không dùng làm khóa duy nhất hoặc thay cho mã bệnh nhân DICOM. Có thể bổ sung CustomerId/AccessionNumber nếu hệ thống cung cấp.

- ID lưu như giá trị opaque; kiểu và tên chính thức theo API thực tế. Không suy diễn ID khách trên web bằng PatientID DICOM.
- Ràng buộc ca với tenant xác thực, agent, khách và đối tượng ngay lúc nhận; không đổi theo màn hình đang được chọn sau đó.
- Server kiểm tra người giao ca và người upload có quyền với tenant/khách/đối tượng. Không tin tenant hoặc model tùy ý trong payload.
- Có định tuyến tới agent cụ thể; server quy định cách đăng ký/ghép agent với phòng khám và xử lý nhiều máy cùng online.
- `requestId` dùng nhận diện giao lại ca; nhận lại cùng ca không tạo ca mới hoặc reset trạng thái đang xử lý.

## 4. UI cần bổ sung

### Đăng nhập và trạng thái kết nối

Ba field trên UI: Tên miền (ví dụ `https://tenant.tdental.vn`), Tài khoản, Mật khẩu. Domain là base URL, không gửi thành field thứ tư trong JSON login.

```http
POST {TenantBaseUrl}/api/Account/Login
Content-Type: application/json
```

```json
{
  "userName": "string",
  "password": "string",
  "rememberMe": true
}
```

Response thành công đã cung cấp có các trường gốc:

```json
{
  "succeeded": true,
  "message": "Authentication succeeded",
  "token": "<access-token-da-che>",
  "refreshToken": "<refresh-token-da-che>",
  "tenantId": "demo1",
  "configs": null,
  "user": {
    "id": "<user-guid>",
    "name": "<display-name>",
    "userName": "<login-name>",
    "companyId": "<company-guid>"
  },
  "userId": null
}
```

Chỉ coi đăng nhập sử dụng được khi HTTP thành công, `succeeded == true` và `token` không rỗng. Giá trị token rỗng trong mẫu đã che không phải token hợp lệ để gọi API. Lưu bảo mật `token`, `refreshToken`, `tenantId` và base URL; chỉ lưu metadata user cần cho UI. Lấy user ID từ `user.id`, không phụ thuộc `userId` vì mẫu là null. Không lưu mật khẩu.

Dùng tenantId làm giá trị dự kiến cho `Attachments.Tenant`, cần đối chiếu backend nếu trường Tenant có ý nghĩa khác. Không suy diễn tenant từ subdomain hoặc companyId. Auth scheme dự kiến Bearer, chưa được xác nhận bằng header/Swagger. Có refreshToken nhưng chưa có endpoint/body refresh nên không tự đặt URL hoặc tự refresh theo suy đoán. `rememberMe: true` không chứng minh token không hết hạn.

- Nhập tên miền, tài khoản, mật khẩu; hiển thị tenant/người dùng/agent đã kết nối.
- Có lỗi cụ thể khi đăng nhập sai, mất mạng, token hết hạn hoặc server chưa hỗ trợ giao ca.
- Phân biệt service DICOM đang chạy với kết nối tenant online; mất Internet không làm mất ảnh đang nhận qua LAN.

### Danh sách yêu cầu

- Hiển thị CustomerName, ResModel, ResId, thời gian, trạng thái và lỗi upload nếu có.
- Chọn ca → xem chi tiết → Bắt đầu nhận ảnh.
- Danh sách tồn tại sau restart; không chỉ giữ trong RAM.

### Ca đang nhận ảnh

- Luôn hiện rõ tenant, khách và đối tượng đích.
- Hiển thị các ảnh/series, metadata, kích thước, thời điểm nhận và trạng thái đối chiếu; chọn/bỏ chọn ảnh gửi.
- Bấm X chỉ ẩn UI, không hoàn tất hoặc tự chuyển ca.
- Nút Xác nhận hiển thị rõ số file và đối tượng sẽ được gắn, sau đó tạo tác vụ upload bền vững.
- Viewer xem pixel không có trong bản hiện tại. Nếu cần xem trước, phải bổ sung viewer DICOM hoặc chuyển đổi theo định dạng API chấp nhận, không giả định trình duyệt hiển thị được `.dcm`.

## 5. Ghép DICOM với ca

- DICOM listener cấu hình IP/port/AE trước và chạy nền liên tục; chọn ca mở phiên gán ảnh, không cần mở/đóng socket theo từng khách. IP listen là IP máy Agent hoặc wildcard, còn console máy chụp được cấu hình gửi tới IP/port/AE này.
- File vẫn được nhận/lưu trước, độc lập với web.
- Khi kỹ thuật viên chọn ca và bấm Bắt đầu nhận ảnh, mở phiên nhận có RequestId/tenant/ResModel/ResId cố định. Event DICOM của nguồn/phiên phù hợp cập nhật danh sách ảnh ngay trên UI ca đó. Không đổi đích gắn file chỉ vì người dùng xem một dòng khác.
- Không có phiên active hoặc không xác định được nguồn/ca thì ảnh vào danh sách chưa gán. Không tự đính kèm ảnh từ mọi thiết bị vào ca đang chọn.
- Đối chiếu PatientID, StudyInstanceUID và mã ca/Accession Number nếu nguồn cung cấp; mã đối chiếu phải được thống nhất với console và server.
- Khi chưa có mã chung, kỹ thuật viên phải chọn/kiểm tra study và xác nhận gán; cảnh báo sai khác metadata, giữ dấu vết thao tác.
- Chỉ một ca active cho mỗi luồng máy chụp ở bản đầu là phương án đề xuất; xử lý nhiều máy/ca đồng thời cần phân luồng nguồn và đối chiếu rõ ràng.
- Sau xác nhận, đóng băng danh sách file upload. Ảnh đến muộn không tự thêm vào ca đã hoàn tất; hiển thị để xử lý bổ sung có chủ đích.

## 6. Upload và phản hồi web

1. Lưu local ca và danh sách file đã xác nhận vào outbox.
2. Gửi HTTPS tới API tenant bằng token và `multipart/form-data`.
3. Gọi `POST {TenantBaseUrl}/api/IrAttachments`, gửi `ResId`, `ResModel`, file part `Attachments.Files` và text part `Attachments.Tenant` theo DTO đã cung cấp. ID khách chỉ gửi nếu DTO có hỗ trợ, không tự thêm thành trường bắt buộc. `IFormFile` là kiểu bind backend; không gửi local path thay cho file.
4. Ghi lại ID file/server receipt cho từng kết quả thành công.
5. Báo trạng thái cho server. Web hiển thị các file đã gắn theo dữ liệu server.

DTO đã xác nhận:

```csharp
public class CreateIrAttachmentsCommand : IRequest
{
    public Guid ResId { get; set; }
    public string ResModel { get; set; }
    public AttachmentUpload Attachments { get; set; }
}

public class AttachmentUpload
{
    public IEnumerable<IFormFile> Files { get; set; } = new List<IFormFile>();
    public string Tenant { get; set; }
}
```

Mapping multipart chuẩn theo DTO:

| Field | Loại/giá trị |
|---|---|
| `ResId` | Text: Guid đối tượng đã gắn với ca |
| `ResModel` | Text: model từ ca đã chọn, đúng giá trị backend chấp nhận |
| `Attachments.Files` | Binary: lặp lại cùng tên part cho mỗi file |
| `Attachments.Tenant` | Text: tenant của phiên đăng nhập ca; dự kiến lấy `tenantId` |

Tên đúng là **Attachments** số nhiều; không dùng `Attachment.Files`, `Attachment.Tenant` hoặc field `Tenant` ở root. Không gửi thêm JSON string tên Attachments thay cho các part. Multipart boundary do HTTP client tạo. Tenant gửi upload phải thuộc phiên đăng nhập của ca, không lấy từ ca khác hoặc suy diễn chỉ từ subdomain.

Ví dụ multipart, Bearer là giả định cần đối chiếu auth scheme thực tế:

```text
POST {TenantBaseUrl}/api/IrAttachments
Authorization: Bearer <token>
Content-Type: multipart/form-data; boundary=<client-generated>

ResId = 11111111-1111-4111-8111-111111111111
ResModel = <model-from-request>
Attachments.Tenant = demo1
Attachments.Files = <binary file 1>
Attachments.Files = <binary file 2>
```

DTO hỗ trợ danh sách file. Còn cần xác nhận giới hạn dung lượng/định dạng, tính nguyên tử khi nhiều file và response thành công/lỗi. Bản Agent hiện nhận `.dcm`; không tự đổi sang JPEG/PNG khi chưa chốt yêu cầu.

Nếu nhiều file chỉ thành công một phần, lưu trạng thái từng file và chỉ retry phần chưa được xác nhận. Không báo toàn bộ ca hoàn tất khi còn file lỗi. Đề xuất khóa chống trùng theo tenant + requestId + SOPInstanceUID/file hash; cần server hỗ trợ idempotency hoặc truy vấn đối chiếu để giải quyết trường hợp upload thành công nhưng mất response. App tự chống gửi lặp không bảo đảm tránh trùng ở server trong trường hợp này.

Nếu upload thành công nhưng cập nhật trạng thái ca lỗi, retry báo trạng thái với các server file ID đã có, không tự upload lại toàn bộ.

## 7. Trạng thái và phục hồi đề xuất

```text
Queued → InProgress → AwaitingConfirmation → UploadPending → Uploading → Completed
                                                           └→ RetryPending
```

- `AwaitingConfirmation`: đã chọn/gán ảnh nhưng kỹ thuật viên chưa xác nhận.
- `UploadPending`: đã xác nhận và lưu tác vụ bền vững, có thể chưa có Internet.
- `RetryPending`: lỗi tạm thời; giữ file và lịch thử lại. Lỗi quyền/validation cần xử lý rõ, không retry vô hạn như lỗi mạng.
- Token hết hạn: refresh nếu server có hợp đồng refresh; nếu không yêu cầu đăng nhập lại. Không lưu mật khẩu để tự login nền.
- Khi restart: phục hồi ca/outbox, đối chiếu kết quả server trước khi gửi lại phần chưa rõ kết quả.
- Hủy ca phải có quy tắc với server; không coi việc đóng cửa sổ hoặc mất kết nối là hủy ca.

## 8. Tenant và bảo vệ thông tin đăng nhập

- Chuẩn hóa base URL HTTPS và kiểm tra TLS; không bỏ kiểm tra chứng chỉ, không gửi token sang origin khác khi redirect.
- Không ghi mật khẩu/token vào log, file cấu hình plaintext hoặc lịch sử lỗi UI. Lưu token trong kho bảo mật phù hợp OS và identity thực hiện upload; xác định rõ Host hay Desktop sở hữu phiên đăng nhập trước triển khai.
- Host hiện chạy Windows service account riêng; token lưu theo user Desktop không mặc định đọc được từ service. Cần thiết kế chuyển giao IPC và lưu secret theo đúng identity, không chỉ dùng một file token chung.
- Đề xuất một tenant active trên một installation ở phiên bản đầu. Mọi ca/file/outbox vẫn gắn tenant bất biến. Đổi tenant/logout không gửi lại ca cũ sang tenant mới; hiển thị các ca chưa đồng bộ và chính sách xử lý.
- Token API web không tự chứng minh quyền điều khiển app. Server cần xác thực/định tuyến agent và cấp quyền nhận ca riêng theo hợp đồng.
- Dữ liệu bệnh nhân có thể có trong DICOM, DB và ảnh preview; kiểm soát cả bản local và dữ liệu gửi lên server.

## 9. Phạm vi thay đổi mã nguồn

| Thành phần | Phần cần thêm |
|---|---|
| Domain | Tenant/session reference, ca chụp, liên kết ca–DICOM, trạng thái upload từng file |
| Application | Contracts login/giao ca/upload, quản lý ca, xác nhận, retry và adapter API |
| Infrastructure | Tenant HTTP client, kênh nhận ca, secure token store, outbox/SQLite migrations |
| Host | Worker nhận ca và đồng bộ; bổ sung IPC cho UI đăng nhập/chọn ca/xác nhận |
| Desktop | Login, danh sách ca, màn hình nhận/chọn ảnh, trạng thái đồng bộ và tray |
| Installer | Đóng gói dependencies/config mới; giữ storage, DB và cấu hình khi nâng cấp |
| Backend/web tenant | Giao ca, đăng ký agent, phân quyền, chống trùng, trạng thái và hiển thị file đã gắn |

Không bỏ phần nhận DICOM, atomic storage hoặc lịch sử hiện có. Không đổi nhận ảnh thành tác vụ bắt buộc phải có Internet. Việc tạo migration/schema cần bảo toàn ảnh và metadata cũ.

## 10. Hợp đồng đang chờ

Đã có response login và DTO upload; không cần cung cấp mật khẩu/token thật. Phần đã biết và còn cần đối chiếu:

1. Login đã biết: POST `/api/Account/Login`, JSON `userName`, `password`, `rememberMe: true`. Response đã biết: succeeded/token/refreshToken/tenantId/user; cần auth scheme, response lỗi và endpoint/body refresh nếu có.
2. Upload đã biết: POST `/api/IrAttachments`, FromForm `ResId` (Guid), `ResModel`, `Attachments.Files` (nhiều file), `Attachments.Tenant` (string). Cần model hợp lệ, ý nghĩa Tenant có đúng tenantId không, giới hạn, response thành công/lỗi và cơ chế trả file IDs nếu có.
3. Giao ca qua server: API/event tạo/nhận/ack/status/reconnect; nếu chưa có thì đề xuất phần server bổ sung.
4. Agent registration/pairing: định danh máy, quyền nhận ca và chọn phòng chụp.
5. Cơ chế server chống trùng và đối chiếu kết quả upload khi timeout.

## 11. Tiêu chí kiểm thử sau triển khai

- Đăng nhập đúng/sai/hết hạn; không lộ secret và không gửi token khác tenant.
- Web ngoài phòng khám tạo ca; đúng agent nhận, máy offline lấy lại ca sau reconnect, giao lại không tạo trùng.
- Chọn đúng khách/đối tượng; ảnh chưa rõ ca và ảnh sai PatientID không tự gắn.
- Xác nhận gửi đúng file và các trường multipart đến API thử nghiệm theo hợp đồng thật; web hiển thị file đã gắn ngay sau khi server xác nhận.
- Lỗi mạng/401/403/validation/file quá lớn/upload một phần/mất response; retry không mất ảnh và không gắn trùng khi server có hỗ trợ cần thiết.
- Restart trong lúc nhận/upload; chuyển tenant/logout khi còn ca pending; ảnh đến muộn sau xác nhận.
- Đóng UI xuống tray và thoát Desktop không làm gián đoạn nhận DICOM; giữ regression tests hiện có.

Chưa có thay đổi runtime hay bộ cài theo thiết kế này. Setup 0.1.1 đã build trước đó chỉ có luồng nhận DICOM/SCU/tray, chưa có đăng nhập tenant, giao ca hay upload.

## 12. Kết quả đối chiếu mã nguồn hiện tại

| Bước người dùng yêu cầu | Hiện trạng app | Thiết kế |
|---|---|---|
| 1. Login bằng domain/tài khoản/mật khẩu | Chưa có màn hình login | Đã bổ sung mô tả 3 field |
| 2. POST login, lưu token local | Chưa có API client hoặc token store | Đã chốt path/body và response mapping; chờ auth scheme/refresh |
| 3. Web giao yêu cầu chụp từ ngoài phòng khám | Chỉ có IPC nội bộ, chưa có API/kênh giao ca | Đề xuất server relay + SignalR, cần backend bổ sung nếu chưa có |
| 4. UI chọn ca, nhận ảnh DICOM | Có receiver và danh sách file chung; chưa có danh sách ca/phiên nhận | Giữ listener nền, thêm session gắn RequestId và event cập nhật UI |
| 5. Bấm Lưu upload FromForm | Chưa có upload, hiện chỉ lưu local | Đã chốt DTO và field multipart; chờ response và giới hạn API |

Nguồn đối chiếu: Desktop MainViewModel/MainWindow, Host Program, ManagementServer, MetadataExtractor và AgentDbContext. Đây là kiểm tra thiết kế/mã nguồn, không phải kiểm thử tenant API trực tiếp. Chưa dùng tài khoản thật hoặc gọi thử vào domain tenant.

Tham khảo kỹ thuật: [SignalR .NET client](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-8.0), [ASP.NET Core file uploads](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0).
