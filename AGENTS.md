# AGENTS.md — Quy tắc làm việc với DicomAgentPlugin

Quy tắc bắt buộc cho mọi agent (Claude Code, IDE agent, script tự động) làm việc trong repository này.

## Quy tắc số 1 — Giữ logic, luôn tạo bộ cài mới

**Mọi thay đổi phải giữ nguyên logic nghiệp vụ hiện tại, và sau khi hoàn thành phải tạo bộ cài cập nhật (Windows Setup.exe + Linux .deb) từ source cuối cùng.**

Cụ thể:

- Không thay đổi API contract, endpoint, header, body, response, CORS, trạng thái request, cách gán ảnh vào ca, xử lý token, hoặc định dạng dữ liệu lưu trữ.
- Không xóa dữ liệu người dùng, không reset cấu hình, không đổi tenant, không migration/backfill dữ liệu tự động.
- Source code và test hiện tại là nguồn đối chiếu hành vi. Nếu phát hiện hành vi có vẻ bất hợp lý, ghi nhận riêng và hỏi, không âm thầm sửa nghiệp vụ trong đợt refactor/tidy.
- Không sửa hoặc bỏ test chỉ để che regression. Bổ sung test có ý nghĩa cho phần thay đổi còn thiếu bằng chứng.
- Khi hoàn thành: tăng patch version (đồng bộ `Directory.Build.props`, script đóng gói, tài liệu), build lại cả hai bộ cài bằng `deploy/windows/build-installer.*` và `deploy/linux/build-deb.sh`, cập nhật checksum, không đóng gói từ binary cũ.

## Kiến trúc — giữ hướng đã có

```
Controller (Host) → Application service/use case → interface (Application/Abstractions) → Infrastructure implementation
```

- Controller chỉ xử lý HTTP: binding, header, response.
- Application giữ nghiệp vụ và điều phối; **không** tham chiếu Infrastructure, ASP.NET Core hoặc fo-dicom.
- Infrastructure thực hiện: JSON storage, bảo vệ token, HTTP upload SaaS, DICOM receiver, SQLite history.
- Desktop (Avalonia) chỉ tham chiếu Application, chỉ làm hiển thị và thao tác UI.
- Host là composition root duy nhất. Host và Desktop là hai tiến trình độc lập, giao tiếp qua HTTP `127.0.0.1`.
- Không thêm framework, pattern, lớp trung gian hoặc interface chỉ để "đúng kiến trúc". Ưu tiên tên rõ nghĩa, hàm có một trách nhiệm, luồng gọi dễ lần theo.

## Bất biến cần bảo toàn khi sửa code

- Web và Desktop dùng HTTP localhost `127.0.0.1`; không WebSocket, Unix socket, named pipe.
- `TenantName` của request đầu tiên gắn cố định với installation, giữ qua restart và xóa request.
- Mỗi request dùng Authorization riêng của request đó khi upload SaaS; không token toàn cục.
- Token không bao giờ xuất hiện plaintext trong log, response, hoặc UI.
- Chỉ chặn tạo mới khi đã có request `new` cùng `resModel + resId + PartnerId` (HTTP 409); request `done` cho phép tạo mới.
- `Idempotency-Key` + body giống nhau = cập nhật token, không tạo request mới, không mất ảnh.
- Không tự retry upload khi server có thể đã nhận ảnh; trạng thái `error` buộc người dùng xác nhận.
- Xóa request hoặc bỏ ảnh khỏi ca không xóa DICOM gốc trong storage.
- Cấu hình Receiver vẫn giữ snapshot lúc khởi động. Theo yêu cầu mới từ 0.2.8, nút Lưu Desktop tự cấp quyền thư mục và restart service qua helper polkit/UAC; API management save cũ giữ nguyên hành vi chỉ lưu. Không hot-mutate snapshot khi đang nhận ảnh.

## Môi trường build/test

- .NET SDK pin `8.0.422` trong `global.json`. Máy dev này có SDK tại `~/.dotnet` — nếu `/usr/bin/dotnet` khác version, dùng `export PATH="$HOME/.dotnet:$PATH"`.
- Chạy tuần tự để tránh xung đột file build:
  ```bash
  dotnet restore DicomAgent.sln
  dotnet build DicomAgent.sln -c Release --no-restore
  dotnet test DicomAgent.sln -c Release --no-build
  ```
- Baseline tại thời điểm viết file này: build sạch, 39 unit + 33 integration test pass.
- Integration test cần quyền tạo TCP loopback; sandbox chặn TCP là lỗi môi trường, không phải lỗi Receiver.

## Code style

- Identifier và comment trong code dùng tiếng Anh. Giải thích cho người dùng bằng tiếng Việt.
- Mỗi dòng một lệnh; không dồn nhiều lệnh lên một dòng (khó đặt breakpoint, khó đọc diff).
- Comment chỉ khi giải thích lý do không rõ từ code (invariant, lý do an toàn, giới hạn đã biết).
